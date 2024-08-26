namespace Aestas.Llms
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Linq
open System.Text
open System.Text.Json
open Aestas
open Aestas.Prim
open Aestas.Core
open Aestas.Core.Logger
open System.Diagnostics
module Gemini =
    type GText = {text: string}
    type _GInlineData = {mime_type: string; data: string}
    type GInlineData = {inline_data: _GInlineData}
    type GPart = 
        | Text of GText 
        | InlineData of GInlineData
    type GContent = {role: string; parts: GPart[]}
    type GSafetySetting = {category: string; threshold: string}
    type GSafetyRatting = {category: string; probability: string}
    type GProfile = {
        api_key: string option
        gcloudpath: string option
        safetySettings: GSafetySetting[]
        generation_configs: IDictionary<string, GenerationConfig> option
        }
    type GGenerationConfig = {
        maxOutputTokens: int
        temperature: float
        topP: float
        topK: float
        }
    type GRequest = {
        contents: GContent[]
        safetySettings: GSafetySetting[]
        systemInstruction: GContent
        generationConfig: GGenerationConfig option
        }
    type GCandidate = {
        content: GContent
        finishReason: string
        index: int
        safetyRatings: GSafetyRatting[]
        }
    type GResponse = {candidates: GCandidate[]}
    let postRequest (auth: GProfile) (url: string) (content: string) =
        async{
            let useOauth = match auth.api_key with | Some _ -> false | None -> true
            let url =
                if useOauth then 
                    logInfo["GeminiOAuth"] "Use OAuth.."
                    url
                else $"{url}?key={auth.api_key.Value}"
            use web = new HttpClient()
            web.BaseAddress <- new Uri(url)
            if useOauth then 
                let access_token = 
                    let info = ProcessStartInfo(auth.gcloudpath.Value, "auth application-default print-access-token")
                    info.RedirectStandardOutput <- true
                    Process.Start(info).StandardOutput.ReadToEnd().Trim()
                logInfo["GeminiOAuth"] "Get access-token successful.."
                web.DefaultRequestHeaders.Authorization <- new AuthenticationHeaderValue("Bearer", access_token)
                web.DefaultRequestHeaders.Add("x-goog-user-project", "")
            let content = new StringContent(content, Encoding.UTF8, "application/json")
            let! response = web.PostAsync("", content) |> Async.AwaitTask
            let result = response.Content.ReadAsStringAsync().Result
            if response.IsSuccessStatusCode then
                return Ok result
            else return Error result
        }
    type GeminiLlm (profile: GProfile, flash: bool) =
        let model = if flash then "gemini-1.5-flash-latest" else "gemini-1.5-pro-latest"
        let generationConfig = 
            match profile.generation_configs with
            | None -> None
            | Some gs -> Some {
                    maxOutputTokens = gs[model].max_length; 
                    temperature = gs[model].temperature;
                    topP = gs[model].top_p;
                    topK = gs[model].top_k
                }
        let contentsCache = Dictionary<AestasChatDomain, arrList<struct(GContent*uint64)>>()
        let parseContents (domain: AestasChatDomain) (contents: AestasContent list) =
            let parts = Array.zeroCreate<GPart> contents.Length
            let iAmMLLM i = function
            | AestasImage(bs, mime, w, h) -> 
                parts[i] <- InlineData {inline_data = {mime_type = mime; data = bs |> Convert.ToBase64String}}
            | x -> 
                parts[i] <- Text {text = Builtin.modelInputConverters domain.Messages x}// !
            contents |> List.iteri iAmMLLM
            {role = "user"; parts = parts}
        interface ILanguageModelClient with
            member this.CacheMessage bot domain message =
                if contentsCache.ContainsKey domain |> not then contentsCache.Add(domain, arrList())
                contentsCache[domain].Add struct(message.content |> parseContents domain, message.mid)
            member this.CacheContents bot domain content =
                if contentsCache.ContainsKey domain |> not then contentsCache.Add(domain, arrList())
                contentsCache[domain].Add struct(content |> parseContents domain, 0UL)
            member this.GetReply bot domain =
                async {
                    let countCache = contentsCache[domain].Count
                    let! response = 
                        let apiLink = 
                            if flash then  "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-latest:generateContent"
                            else "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro-latest:generateContent"
                        let temp = Array.init (contentsCache[domain].Count) (fun i -> fst' (contentsCache[domain][i]))
                        let system =
                            {role = "system"; parts = [|Text {text = bot.SystemInstruction}|]}
                        let messages = 
                            {contents = temp; safetySettings = profile.safetySettings; systemInstruction = system; generationConfig = generationConfig}
                        postRequest profile apiLink (jsonSerialize messages)
                    match response with
                    | Ok result -> 
                        let response = 
                            try
                            match jsonDeserialize<GResponse>(result).candidates[0].content.parts[0] with
                            | Text t -> t.text.Replace("\n\n", "\n")
                            | InlineData _ -> ""
                            with ex -> 
                                logError[0] $"Gemini response parse failed: {ex}"
                                "..."
                        return response.TrimEnd() |> Builtin.modelOutputParser bot domain |> Ok, 
                        fun msg -> 
                            contentsCache[domain].Insert(countCache, struct({role = "model"; parts = [|Text {text = response}|]}, msg.mid))
                    | Error result ->
                        logError[0] $"Gemini request failed: {result}" 
                        return Error result, ignore
                }
            member _.ClearCache() = contentsCache.Clear()
            member _.RemoveCache domain messageID =
                match
                    contentsCache[domain] |>
                    ArrList.tryFindIndexBack (fun x -> snd' x = messageID) 
                with
                | Some i -> contentsCache[domain].RemoveAt i
                | None -> ()


// type G10GenerationConfig = {maxOutputTokens: int; temperature: float; topP: float}
// type G10Request = {contents: ResizeArray<GContent>; safetySettings: GSafetySetting[]; generationConfig: G10GenerationConfig option}
// type Gemini10Client(profile: string, tunedName: string) =
//     let apiLink = 
//         if tunedName |> String.IsNullOrEmpty then "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.0-pro:generateContent"
//         else $"https://generativelanguage.googleapis.com/v1beta/tunedModels/{tunedName}:generateContent"
//     let chatInfo = 
//         use file = File.OpenRead(profile)
//         use reader = new StreamReader(file)
//         let json = reader.ReadToEnd()
//         jsonDeserialize<GProfile>(json)
//     let messages = {
//             contents = ResizeArray(); 
//             safetySettings = chatInfo.safetySettings; 
//             generationConfig = 
//                 match chatInfo.generation_configs with
//                 | None -> None
//                 | Some gs -> Some {
//                         maxOutputTokens = gs["gemini-1.0-pro"].max_length; 
//                         temperature = gs["gemini-1.0-pro"].temperature;
//                         topP = gs["gemini-1.0-pro"].top_p
//                 }
//             }
//     let _ = 
//         messages.contents.Add({role = "user"; parts = [|{text = chatInfo.system}|]})
//         messages.contents.Add({role = "model"; parts = [|{text = "Certainly!"}|]})
//     let database = 
//         match chatInfo.database with
//         | Some db -> ResizeArray(db)
//         | None -> ResizeArray()
//     let checkDialogLength () =
//         match chatInfo.generation_configs with
//         | None -> ()
//         | Some config ->
//         let rec trim (messages: ResizeArray<GContent>) sum =
//             if sum > config["gemini-1.0-pro"].context_length && messages.Count <> 2 then
//                 let temp = messages[2].parts[0].text.Length+messages[3].parts[0].text.Length
//                 messages.RemoveRange(2, 2)
//                 trim messages (sum-temp)
//             else ()
//         let sum = (messages.contents |> Seq.sumBy (fun m -> m.parts[0].text.Length))
//         trim messages.contents sum
//     let receive (input: string) send =
//         async {
//         let! response = 
//             let messages = 
//                 // gemini tuned model not supported multiturn chat yet, so we need do that manually
//                 if tunedName |> String.IsNullOrEmpty then
//                     let temp = arrList(messages.contents)
//                     temp[0] <- {role = "user"; parts = [|{text = buildDatabasePrompt temp[0].parts[0].text database}|]}
//                     temp.Add {role = "user"; parts = [|{text = input}|]}
//                     {contents = temp; safetySettings = chatInfo.safetySettings; generationConfig = messages.generationConfig}
//                 else 
//                     // not support database because i am lazy
//                     let sb = StringBuilder()
//                     for c in messages.contents do
//                         sb.Append '{' |> ignore
//                         sb.Append c.role |> ignore
//                         sb.Append '}' |> ignore
//                         sb.Append ':' |> ignore
//                         sb.Append(c.parts[0].text) |> ignore
//                         sb.Append '\n' |> ignore
//                     sb.Append "{user}:" |> ignore
//                     sb.Append input |> ignore
//                     sb.Append "\n{model}:" |> ignore
//                     {contents = ResizeArray([{role = "user"; parts = [|{text = sb.ToString()}|]}]); safetySettings = chatInfo.safetySettings; generationConfig = messages.generationConfig}
//             Gemini.postRequest chatInfo apiLink (jsonSerialize(messages))
//         match response with
//         | Ok result ->
//             let response = jsonDeserialize<GResponse>(result).candidates[0].content.parts[0].text
//             do! send response
//             messages.contents.Add {role = "user"; parts = [|{text = input}|]}
//             messages.contents.Add {role = "model"; parts = [|{text = response}|]}
//             checkDialogLength()
//         | Error result -> printfn "Gemini10 request failed: %A" result
//         }
//     interface IChatClient with
//         member _.Messages = 
//             let r = 
//                 messages.contents 
//                 |> Seq.map (fun m -> {role = m.role; content = m.parts[0].text}) 
//                 |> ResizeArray
//             r.RemoveRange(0, 2)
//             r
//         member _.DataBase = database
//         member _.Turn input send = receive input send |> Async.RunSynchronously
//         member _.TurnAsync input send = receive input send