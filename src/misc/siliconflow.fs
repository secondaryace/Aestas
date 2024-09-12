namespace Aestas.Misc
open System
open System.Net.Http
open System.Text.Json.Nodes
open Aestas.Prim
open Aestas.Core

module SiliconFlow =
    type SiliconFlowAPI = {
        endpoint:string
        key: string
        }
    let textToImageMethod apiKey argument =
        async {
            let client = new HttpClient()
            let request = new HttpRequestMessage(HttpMethod.Post, apiKey.endpoint)
            request.Headers.Add("accept", "application/json")
            request.Headers.Add("authorization", $"Bearer {apiKey.key}")
            let seed = match argument.seed with Some i -> $",\n\"seed\": \"{i}\"" | None -> ""
            let argument = $"""{{
  "prompt": "{argument.prompt}",
  "image_size": "{fst argument.resolution}x{snd argument.resolution}",
  "num_inference_steps": 20{seed}
}}"""
            request.Content <- new StringContent(argument, Text.Encoding.UTF8, "application/json")
            let! result = client.SendAsync(request) |> Async.AwaitTask
            if result.IsSuccessStatusCode then 
                let! content = result.Content.ReadAsStringAsync() |> Async.AwaitTask
                let json = jsonDeserialize<JsonObject> content
                return! bytesFromUrlAsync (json["images"].[0].["url"].ToString())
            else
                return Error result.ReasonPhrase
        }
