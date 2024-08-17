namespace Aestas.Plugins
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Text
open System.Text.Json
open Aestas.Core
open Aestas.AutoInit
open Aestas.Prim

module MsTts =
    type MsTts_Profile = {subscriptionKey: string; subscriptionRegion: string; voiceName: string; outputFormat: string;}
    let getVoice (profile: MsTts_Profile) (content: (string*string)[]) =
            let url = $"https://{profile.subscriptionRegion}.tts.speech.microsoft.com/cognitiveservices/v1"
            use web = new HttpClient()
            web.BaseAddress <- new Uri(url)
            web.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", profile.subscriptionKey)
            web.DefaultRequestHeaders.Add("X-Microsoft-OutputFormat", profile.outputFormat)
            web.DefaultRequestHeaders.Add("User-Agent", "HttpClient")
            let ssmlHead = $"""
    <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xmlns:mstts="https://www.w3.org/2001/mstts" xml:lang="zh-CN">
        <voice name='{profile.voiceName}'>"""
            let ssmlVoice style dialog = $"""
            <mstts:express-as style='{style}'>
                {dialog}
            </mstts:express-as>"""
            let ssmlTail = $"""
        </voice>
    </speak>
    """
            let ssmlBuilder = StringBuilder()
            ssmlBuilder.Append ssmlHead |> ignore
            content |> Array.iter (fun (style, dialog) -> ssmlBuilder.Append(ssmlVoice style dialog) |> ignore)
            ssmlBuilder.Append ssmlTail |> ignore
            let content = 
                new StringContent(ssmlBuilder.ToString(), Encoding.UTF8, "application/ssml+xml")
            Logger.logInfo[0] (ssmlBuilder.ToString())
            content.Headers.ContentType <- MediaTypeHeaderValue("application/ssml+xml")
            let response = web.PostAsync("", content).Result
            if response.IsSuccessStatusCode then
                response.Content.ReadAsByteArrayAsync().Result
            else
                Logger.logInfo[0] $"MsTts Error: {response.ReasonPhrase}"
                raise <| new Exception(response.ReasonPhrase)
    type MsTtsContent(content: (string*string)[]) =
        interface IAestasMappingContent with
            member _.ContentType = "voice"
            member _.Convert bot domain = 
                match bot.ExtraData("mstts") with
                | Some (:? MsTts_Profile as profile) -> 
                    let voice = getVoice profile content
                    AestasAudio(voice, "audio/ogg")
                | _ -> AestasText("Error: No mstts profile found")
        interface IAutoInit<(ContentParam->IAestasMappingContent)*string*string, unit> with
            static member Init _ = 
                (fun (domain, params', content) ->
                    params' |> List.rev |> Array.ofList |> MsTtsContent :> IAestasMappingContent), "voice", 
                """You may send voice messages like #[voice@emotion0=content0@emotion1=content1...].
emotion can be one of Default, gentle, embarrassed, sad, cheerful, affectionate, angry.
e.g. #[voice@cheerful=Hello, World!@sad=Goodbye, World!]"""
            