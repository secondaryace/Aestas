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
open Aestas.Core.Builtin
open Aestas.Core.Logger
open Aestas.Core.AestasBot
open System.Diagnostics

module Gemini =
    type GText = {text: string}
    type _GInlineData = {mime_type: string; data: string}
    type GInlineData = {inline_data: _GInlineData}
    type GPart = 
        | Text of GText 
        | InlineData of GInlineData
    type GContent = {role: string; parts: IList<GPart>}
    type GSafetySetting = {category: string; threshold: string}
    type GSafetyRatting = {category: string; probability: string}
    type GProfile = {
        apiKey: string option
        gcloudPath: string option
        safetySettings: GSafetySetting[]
        generation_configs: GenerationConfig option
        }
    type GGenerationConfig = {
        maxOutputTokens: int option
        temperature: float option
        topP: float option
        topK: int option
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
        // let it be optional for 002 models
        index: int option
        safetyRatings: GSafetyRatting[]
        }
    type GResponse = {candidates: GCandidate[]}
    let postRequest (auth: GProfile) (url: string) (content: string) =
        async{
            
            match auth.apiKey, auth.gcloudPath with None, None -> failwith "Gemini: apiKey and gcloudPath can't be both None" | _ -> ()
            let useOauth = match auth.apiKey with | Some _ -> false | None -> true
            let url =
                if useOauth then 
                    logInfo["GeminiOAuth"] "Use OAuth.."
                    url
                else $"{url}?key={auth.apiKey.Value}"
            use web = new HttpClient()
            web.BaseAddress <- new Uri(url)
            if useOauth then 
                let access_token = 
                    let info = ProcessStartInfo(auth.gcloudPath.Value, "auth application-default print-access-token")
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
    type GeminiLlm (profile: GProfile, model: string) =
        let generationConfig = 
            match profile.generation_configs with
            | None -> None
            | Some gs -> Some {
                    maxOutputTokens = gs.maxLength; 
                    temperature = gs.temperature;
                    topP = gs.topP;
                    topK = gs.topK
                }
        let contentsCache = Dictionary<AestasChatDomain, struct(GContent*uint64) arrList>()
        let rec parseContents (bot: AestasBot) (domain: AestasChatDomain) (contents: AestasContent list) =
            let parts = arrList<GPart>()
            let parse i = function
            | AestasImage img -> 
                parts.Add <| InlineData {inline_data = {mime_type = img.mimeType; data = img.data |> Convert.ToBase64String}}
            | AestasQuote mid ->
                match contentsCache[domain] |> ArrList.tryFindBack (snd' >> (=) mid) with
                | Some (msg, _) when msg.role = "model" -> 
                    parts.Add <| Text {text = "#[quote:" }
                    parts.AddRange msg.parts
                    parts.Add <| Text {text = "]" }
                | Some (_, mid) ->
                    parts.Add <| Text {text = "#[quote:" }
                    match domain.Messages |> IList.tryFindBack (fun m -> m.MessageId = mid) with
                    | Some msgAdapter ->
                        parts.Add <| Text {text = "#[quote:" }
                        ((bot.ParseIMessageAdapter domain true msgAdapter).contents 
                        |> parseContents bot domain).parts |> parts.AddRange
                        parts.Add <| Text {text = "]" }
                    | _ -> parts.Add <| Text {text = "#[quote: ...]" }
                | None -> parts.Add <| Text {text = "#[quote: ...]" }
            | AestasFold ms ->
                if ms.Count < 15 then
                    parts.Add <| Text {text = "#[fold:" }
                    ms |> IList.map (fun m ->
                        bot.ParseIMessageAdapter domain true m
                        |> _.contents
                        |> parseContents bot domain 
                        |> _.parts)
                    |> IList.iter parts.AddRange
                    parts.Add <| Text {text = "]" }
                else parts.Add <| Text {text = $"#[fold: {ms.Count} messages]" }
            | x -> 
                parts.Add <| Text {text = modelInputConverters domain x}// !
            contents |> List.iteri parse
            {role = "user"; parts = parts}
        
        let dumpCommand() = {
            name = "dump"
            description = "Dump cached context of gemini"
            accessibleDomain = CommandAccessibleDomain.All
            privilege = CommandPrivilege.Normal
            execute = fun executer env args -> 
                let sb = StringBuilder()
                sb.Append "## Gemini Context" |> ignore
                contentsCache 
                |> Dict.tryGetValue env.domain 
                |> Option.iter (fun x ->
                    x |>
                    ArrList.iter (fun struct(content, mid) ->
                        sprintf "\n*%d\n  %A" mid content |> sb.Append |> ignore))
                sb.ToString() |> env.log
            }
        interface ILanguageModelClient with
            member this.Bind bot =
                addCommandExecuter bot "\\gemini:" (SpacedTextCommandExecuter([dumpCommand(); helpCommand()]))
            member this.UnBind bot =
                removeCommandExecuter bot "\\gemini:" |> ignore
            member this.CacheMessage bot domain message =
                if contentsCache.ContainsKey domain |> not then contentsCache.Add(domain, arrList())
                contentsCache[domain].Add struct(message.contents |> parseContents bot domain, message.mid)
            member this.CacheContents bot domain content =
                if contentsCache.ContainsKey domain |> not then contentsCache.Add(domain, arrList())
                contentsCache[domain].Add struct(content |> parseContents bot domain, 0UL)
            member this.GetReply bot domain =
                async {
                    let countCache = contentsCache[domain].Count
                    let! response = 
                        let apiLink = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
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
                                logErrorf[0] "Gemini response parse failed: %A, %s" ex result
                                "..."
                        return response.TrimEnd() |> bot.ModelOutputParser bot domain |> Ok, 
                        fun msg -> 
                            contentsCache[domain].Insert(countCache, struct({role = "model"; parts = [|Text {text = response}|]}, msg.mid))
                    | Error result ->
                        logErrorf[0] "Gemini response failed: %s" result
                        return Error result, ignore
                }
            member _.ClearCache domain = if contentsCache.ContainsKey domain then contentsCache[domain].Clear()
            member _.RemoveCache domain messageId =
                match
                    contentsCache[domain] |>
                    ArrList.tryFindIndexBack (snd' >> (=) messageId) 
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