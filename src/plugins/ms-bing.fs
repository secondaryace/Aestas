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

module MsBing =
    let queryParameter = "?q="  // Required
    let mktParameter = "&mkt="  // Strongly suggested
    let responseFilterParameter = "&responseFilter="
    let countParameter = "&count="
    let offsetParameter = "&offset="
    let freshnessParameter = "&freshness="
    let safeSearchParameter = "&safeSearch="
    let textDecorationsParameter = "&textDecorations="
    let textFormatParameter = "&textFormat="
    let answerCountParameter = "&answerCount="
    let promoteParameter = "&promote="
    type MsBing_Profile = {subscriptionKey: string; baseUri: string; mkt: string}
    let search (profile: MsBing_Profile) (searchString: string) =
        let queryString = 
            queryParameter + searchString + mktParameter + profile.mkt + textDecorationsParameter + "true"
        use web = new HttpClient()
        web.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", profile.subscriptionKey)
        web.DefaultRequestHeaders.Add("User-Agent", "HttpClient")
        Logger.logInfo[0] searchString
        let response = web.GetAsync(profile.baseUri + queryString).Result
        if response.IsSuccessStatusCode then
            let content = response.Content.ReadAsStringAsync().Result
            jsonDeserialize<Nodes.JsonObject> content
        else
            Logger.logInfo[0] $"MsBing Error: {response.ReasonPhrase}"
            raise <| new Exception(response.ReasonPhrase)
    type MsBingContent(searchString: string) =
        interface IAestasMappingContent with
            member _.Convert bot domain = 
                match bot.ExtraData("msbing") with
                | Some (:? MsBing_Profile as profile) -> 
                    let result = search profile searchString
                    let sb = StringBuilder()
                    sb.Append("#[Bing search result:\n") |> ignore
                    (result["webPages"]["value"]).AsArray()
                    |> IListExt.iter (fun x -> sb.Append(x["snippet"]).Append("(").Append(x["name"]).Append(")\n") |>ignore)
                    sb.Append("]") |> ignore
                    Some [sb.ToString() |> AestasText]
                    |> bot.SelfTalk domain
                    |> Async.Ignore
                    |> Async.Start
                    AestasText ""
                | _ -> AestasText("Error: No msbing profile found")
        interface IAutoInit<(ContentParam -> IAestasMappingContent)*string*(AestasBot -> string), unit> with
            static member Init _ = 
                (fun (domain, params', content) ->
                    content |> MsBingContent :> IAestasMappingContent), "bing", (fun _ ->
                """You may use bing web search by using format like #[bing:search string].
Then this function will return a list of search results.
e.g. #[bing: weather of ShenZhen]""")
            