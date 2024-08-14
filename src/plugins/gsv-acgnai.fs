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

module Gsv_Acgnai =
    type Gsv_Acgnai_Profile = {token: string; speaker: string}
    // {
    //     "access_token": "你的访问令牌",
    //     "type": "tts",
    //     "brand": "gpt-sovits",
    //     "name": "anime",
    //     "method": "api",
    //     "prarm": {
    //         "speaker": "芙宁娜",
    //         "emotion": "中立",
    //         "text": "测试语音合成。",
    //         "text_language":"中文",
    //         "audio_url": "https://gsv.ai-lab.top",
    //         "top_k": 10,
    //         "top_p": 1.0,
    //         "temperature": 1.0,
    //         "speed": 1.0
    //     }
    // }
    type Gsv_AcgnaiPrarm = {speaker: string; emotion: string; text: string; text_language: string; audio_url: string; top_k: int; top_p: float; temperature: float; speed: float}
    type Gsv_AcgnaiRequest = {access_token: string; [<Serialization.JsonPropertyName("type")>]type': string; brand: string; name: string; method: string; prarm: Gsv_AcgnaiPrarm}
    type Gsv_AcgnaiResponse = {message: string; audio: string}
    let getVoice (profile: Gsv_Acgnai_Profile) (content: string*string) =
            let url = "https://infer.acgnai.top/infer/gen"
            use web = new HttpClient()
            web.BaseAddress <- new Uri(url)
            let content = $"""{{
        "access_token": "{profile.token}",
        "type": "tts",
        "brand": "gpt-sovits",
        "name": "anime",
        "method": "api",
        "prarm": {{
            "speaker": "{profile.speaker}",
            "emotion": "{fst content}",
            "text": "{snd content}",
            "text_language":"自动检测",
            "audio_url": "https://gsv.ai-lab.top",
            "top_k": 10,
            "top_p": 1.0,
            "temperature": 1.0,
            "speed": 1.0
    }}
}}"""
            Logger.logInfo[0] content
            let content = new StringContent(content, Encoding.UTF8, "application/json")
            let response = web.PostAsync("", content).Result
            if response.IsSuccessStatusCode then
                let result = response.Content.ReadAsByteArrayAsync().Result |> Encoding.UTF8.GetString
                let url = (result |> jsonDeserialize<Gsv_AcgnaiResponse>).audio
                bytesFromUrl url
            else
                Logger.logInfo[0] $"Gsv_Acgnai Error: {response.ReasonPhrase}"
                raise <| new Exception(response.ReasonPhrase)
    type Gsv_AcgnaiContent(content: string*string) =
        interface IAestasMappingContent with
            member _.ContentType = "gsvVoice"
            member _.Convert bot domain = 
                match bot.ExtraData("gsv") with
                | Some (:? Gsv_Acgnai_Profile as profile) -> 
                    let voice = getVoice profile content
                    AestasAudio(voice, "audio/wav")
                | _ -> AestasText("Error: No gsv profile found")
        interface IAutoInit<(ContentParam->IAestasMappingContent)*string*string, unit> with
            static member Init _ = 
                (fun (domain, params', content) ->
                    params'.Head |> Gsv_AcgnaiContent :> IAestasMappingContent), "gsvVoice", 
                """You may send voice messages like #[gsvVoice@emotion=content].
emotion can be one of "难过_sad", "生气_angry", "开心_happy", "中立_neutral".
e.g. #[gsvVoice@开心_happy=你好呀！]"""
            