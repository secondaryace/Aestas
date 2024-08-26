//! nuget LLamaSharp=0.15.0
//! nuget LLamaSharp.Backend.Cuda12=0.15.0
namespace Aestas.Llms
open LLama
open LLama.Common
open LLama.Native
open System.Collections.Generic
open Aestas
open Aestas.Prim
open Aestas.Core
open System.Text

module LocalLLama =
    [<Struct>]
    type LLamaMessageCache = {role: string; message: string; mid: uint64}
    let models = new Dictionary<string, LLamaWeights>()
    let loadModel (modelPath: string) parameters =
        if models.ContainsKey modelPath then models[modelPath] else
        let model = LLamaWeights.LoadFromFile parameters
        models.Add(modelPath, model)
        model
    let mutable logRedirectFlag = true
    type LocalLLamaLlm (generationConfig: GenerationConfig, modelPath: string, gpuLayerCount: int, contextLength: uint32) =
        do if logRedirectFlag then
            NativeLibraryConfig.All.WithLogCallback(fun level message -> ()) |> ignore
            logRedirectFlag <- false
        let parameters = new ModelParams(modelPath)
        do
            parameters.ContextSize <- contextLength 
            parameters.GpuLayerCount <- gpuLayerCount
        let model =
            loadModel modelPath parameters
        let contentsCache = Dictionary<AestasChatDomain, LLamaMessageCache arrList>()
        let parseContents (domain: AestasChatDomain) (contents: AestasContent list) =
            let sb = StringBuilder()
            contents |> List.iter (fun x -> x |> Builtin.modelInputConverters domain.Messages |> sb.Append |> ignore)
            let s = sb.ToString()
            s
        let buildContext (system: string) domain =
            let sb = StringBuilder()
            sb.Append "<|begin_of_text|>" |> ignore
            sb.Append "<|start_header_id" |> ignore
            sb.Append "system" |> ignore
            sb.Append "<|end_header_id|>\n" |> ignore
            sb.Append system |> ignore
            sb.Append "<|eot_id|>" |> ignore
            contentsCache[domain] 
            |> ArrList.iter (fun x ->
                sb.Append "<|start_header_id" |> ignore
                sb.Append x.role |> ignore
                sb.Append "<|end_header_id|>\n" |> ignore
                sb.Append x.message |> ignore
                sb.Append "<|eot_id|>" |> ignore)
            sb.Append "<|start_header_id" |> ignore
            sb.Append "assistant" |> ignore
            sb.Append "<|end_header_id|>\n" |> ignore
            sb.ToString()
        let infer s =
            use context = model.CreateContext parameters
            let executor = new InteractiveExecutor(context)
            let inferenceParams = new InferenceParams()
            inferenceParams.MaxTokens <- generationConfig.max_length
            inferenceParams.AntiPrompts <- [|"<|eot_id|>";|]
            let pipeline = new Sampling.DefaultSamplingPipeline()
            pipeline.Temperature <- generationConfig.temperature |> float32
            inferenceParams.SamplingPipeline <- pipeline
            let sb = new StringBuilder()
            let aitor = executor.InferAsync(s, inferenceParams).GetAsyncEnumerator()
            while aitor.MoveNextAsync().AsTask() |> Async.AwaitTask |> Async.RunSynchronously do
                let response = aitor.Current
                sb.Append response |> ignore
            sb.ToString()
        interface ILanguageModelClient with
            member this.CacheMessage bot domain message =
                if contentsCache.ContainsKey domain |> not then contentsCache.Add(domain, arrList())
                contentsCache[domain].Add {role = "user"; message = message.content |> parseContents domain; mid = message.mid}
            member this.CacheContents bot domain content =
                if contentsCache.ContainsKey domain |> not then contentsCache.Add(domain, arrList())
                contentsCache[domain].Add {role = "user"; message = content |> parseContents domain; mid = 0UL}
            member this.GetReply bot domain =
                async {
                    let countCache = contentsCache[domain].Count
                    let response = (buildContext bot.SystemInstruction domain |> infer).Replace("<|eot_id|>", "").Replace("\uFFFD", "")
                    return response.TrimEnd() |> Builtin.modelOutputParser bot domain |> Ok, 
                        fun msg -> 
                            contentsCache[domain].Insert(countCache, {role = "assistant"; message = response; mid = msg.mid})
                }
            member _.ClearCache() = contentsCache.Clear()
            member _.RemoveCache domain messageID =
                match
                    contentsCache[domain] |>
                    ArrList.tryFindIndexBack (fun x -> x.mid = messageID) 
                with
                | Some i -> contentsCache[domain].RemoveAt i
                | None -> ()