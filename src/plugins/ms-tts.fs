//! nuget StbVorbisSharp=1.22.4
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

open StbVorbisSharp

module MsTts =
    type MsTts_Profile = {subscriptionKey: string; subscriptionRegion: string; voiceName: string; outputFormat: string; styles: string list}
    let inline private getMime s =
        match s with
        | "ogg-16khz-16bit-mono-opus"
        | "ogg-24khz-16bit-mono-opus"
        | "ogg-48khz-16bit-mono-opus" -> "audio/ogg"
        | "amr-wb-16000hz" -> "audio/amr-wb"
        | _ -> "audio/mp3"
    let inline private getDuration s data =
        match s with
        | "ogg-16khz-16bit-mono-opus"
        | "ogg-24khz-16bit-mono-opus"
        | "ogg-48khz-16bit-mono-opus" -> 
            let vorbis = Vorbis.FromMemory data
            int(vorbis.LengthInSeconds)
        | _ -> 0
    let getVoice (profile: MsTts_Profile) (content: (string*string) list) =
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
    </speak>"""
        let ssmlBuilder = StringBuilder()
        ssmlBuilder.Append ssmlHead |> ignore
        content |> List.iter (fun (style, dialog) -> ssmlBuilder.Append(ssmlVoice style dialog) |> ignore)
        ssmlBuilder.Append ssmlTail |> ignore
        let content = 
            new StringContent(ssmlBuilder.ToString(), Encoding.UTF8, "application/ssml+xml")
        Logger.logInfo[0] (ssmlBuilder.ToString())
        content.Headers.ContentType <- MediaTypeHeaderValue("application/ssml+xml")
        let response = web.PostAsync("", content).Result
        if response.IsSuccessStatusCode then
            response.Content.ReadAsByteArrayAsync().Result |> Ok
        else
            Error response.ReasonPhrase
    type MsTtsParser =
        interface IAutoInit<string*ContentParser*(AestasBot -> StringBuilder -> unit), unit> with
            static member Init _ = 
                "voice"
                , fun bot domain params' content ->
                    match bot.TryGetExtraData("mstts") with
                    | Some (:? MsTts_Profile as profile) -> 
                        match params' |> List.rev |> getVoice profile with
                        | Ok voice -> AestasAudio(voice, getMime profile.outputFormat, getDuration profile.outputFormat voice) |> Ok
                        | Error emsg -> Error emsg
                    | _ -> Error "Couldn't find mstts data"
                , fun bot builder ->
                    builder.AppendLine "You may send voice messages like #[voice@emotion0=content0@emotion1=content1...]." |> ignore
                    builder.Append "emotion can be one of: Default" |> ignore
                    match bot.TryGetExtraData("mstts") with
                    | Some (:? MsTts_Profile as profile) -> 
                        profile.styles |> List.iter (fun style -> builder.Append $", {style}" |> ignore)
                        builder.AppendLine "." |> ignore
                        match profile.styles with
                        | s0::s1::_ ->
                            builder.Append("e.g. #[voice@)").Append(s0).Append("=Hello, World!@")
                                .Append(s1).Append("=Goodbye, World!]") |> ignore
                        | _ -> ()
                    | _ -> ()