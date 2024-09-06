namespace Aestas.Llms
open System
open System.Collections.Generic
open System.Text
open System.Net.Http
open Aestas.Prim
open Aestas.Core
open Aestas.Llms.SimpleOddRequiredTemplateLlm

module Ernie =
    type ErnieMessage = 
        {role: string; content: string}
        interface ISimpleOddRequiredTemplateLlmMessage with
            member this.Role = this.role
            member this.Content = this.content
            member this.Bind f = {role = this.role; content = f this.content}
    type ErniePayload = {messages: ErnieMessage arrList; temperature: float; top_p: float; system: string}
    type ErnieResponse = {
            result: string
            is_truncated: bool
            need_clear_history: bool
        }
    let getUrl modelType accessToken = $"https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/chat/{modelType}?access_token={accessToken}"
    let accessToken apiKey secretKey =
        let url = $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={apiKey}&client_secret={secretKey}"
        use web = new HttpClient()
        let content = new StringContent("{}", Encoding.UTF8, "application/json")
        let response = web.PostAsync(url, content).Result
        let result = response.Content.ReadAsStringAsync().Result
        (jsonDeserialize<Json.Nodes.JsonObject>(result)["access_token"]).ToString()
    let messageCtor bot domain role message = 
        {role = role; content = message}        
    let getReplyFromResponse response = 
        try
            (response |> jsonDeserialize<ErnieResponse>).result
        with ex ->
            Logger.logError[0] ex.Message
            "..."
    let payloadCtor (generationConfig: GenerationConfig) (bot: AestasBot) domain messages =
        {
            messages = messages
            temperature = generationConfig.temperature
            top_p = generationConfig.topP
            system = bot.SystemInstruction
        }
    type ErnieLlm(generationConfig: GenerationConfig, modelType: string, apiKey: string, secretKey: string) =
        inherit SimpleOddRequiredTemplateLlm<ErnieMessage, ErniePayload>("user", "assistant", "system", 
            messageCtor, payloadCtor generationConfig, getReplyFromResponse, fun () -> accessToken apiKey secretKey |> getUrl modelType)
   