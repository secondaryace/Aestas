namespace Aestas.Llms
open System
open System.Collections.Generic
open System.Text
open System.Net.Http
open Aestas.Prim
open Aestas.Core

module SimpleOddRequiredTemplateLlm = 
    type ISimpleOddRequiredTemplateLlmMessage =
        abstract member Role: string
        abstract member Content: string
        abstract member Bind: (string -> string) -> ISimpleOddRequiredTemplateLlmMessage
    type SimpleOddRequiredTemplateLlm<'message, 'payload when 'message :> ISimpleOddRequiredTemplateLlmMessage>(
        userRole, modelRole, systemRole,
        messageCtor: AestasBot -> AestasChatDomain -> string -> string -> 'message,
        payloadCtor: AestasBot -> AestasChatDomain -> 'message arrList -> 'payload,
        getReplyFromResponse: string -> string,
        getUrl: unit -> string) =
        let contentsCache = Dictionary<AestasChatDomain, struct('message*uint64) arrList>()
        let buildMessageString domain content = 
            content |> List.map (Builtin.modelInputConverters domain) |> String.concat " "
        interface ILanguageModelClient with
            member this.CacheContents bot domain contents =
                if contentsCache.ContainsKey domain |> not then contentsCache.Add(domain, arrList())
                if contentsCache[domain].Count <> 0 && (fst' (contentsCache[domain][^0])).Role = userRole then
                    contentsCache[domain][^0] <- 
                        (fst' (contentsCache[domain][^0])).Bind (fun x -> $"{x}\n{contents |> buildMessageString domain}") :?> 'message,
                        snd' (contentsCache[domain][^0])
                else
                    contentsCache[domain].Add(contents |> buildMessageString domain |> messageCtor bot domain userRole, 0UL)
            member this.CacheMessage bot domain message =
                if contentsCache.ContainsKey domain |> not then contentsCache.Add(domain, arrList())
                if contentsCache[domain].Count <> 0 && (fst' (contentsCache[domain][^0])).Role = userRole then
                    contentsCache[domain][^0] <- 
                        (fst' (contentsCache[domain][^0])).Bind (fun x -> $"{x}\n{message.content |> buildMessageString domain}") :?> 'message,
                        snd' (contentsCache[domain][^0])
                else
                    contentsCache[domain].Add(message.content |> buildMessageString domain |> messageCtor bot domain userRole, message.mid)
            member this.RemoveCache domain messageId = 
                match
                    contentsCache[domain] |>
                    ArrList.tryFindIndexBack (fun x -> snd' x = messageId) 
                with
                | Some i -> contentsCache[domain].RemoveAt i
                | None -> ()
            member _.ClearCache domain = if contentsCache.ContainsKey domain then contentsCache[domain].Clear()
            member this.GetReply bot domain =
                async {
                    let web = new HttpClient()
                    let payload = contentsCache[domain] |> ArrList.map fst' |> payloadCtor bot domain
                    let cachedMid = contentsCache[domain][^0] |> snd'
                    let content = new StringContent(jsonSerialize payload, Encoding.UTF8, "application/json")
                    //Logger.logInfo[0] (jsonSerialize payload)
                    let! response = web.PostAsync(getUrl(), content) |> Async.AwaitTask
                    if response.IsSuccessStatusCode then
                        let! result = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        //Logger.logInfo[0] (jsonSerialize result)
                        let reply = getReplyFromResponse result |> Builtin.modelOutputParser bot domain
                        return Ok reply, 
                        fun msg ->
                            match contentsCache[domain] |> ArrList.tryFindIndexBack (fun struct(_, x) -> x = cachedMid) with
                            | Some i ->
                                contentsCache[domain].Insert(i+1, struct(msg.content |> buildMessageString domain |> messageCtor bot domain modelRole, msg.mid))
                            | None ->
                                match contentsCache[domain] |> ArrList.tryFindIndexBack (fun struct(m, _) -> m.Role = userRole) with
                                | Some i -> 
                                    contentsCache[domain].Insert(i+1, 
                                        struct(msg.content |> buildMessageString domain |> messageCtor bot domain modelRole, msg.mid))
                                | None -> failwith "Bad condition"
                    else return Error "Failed", ignore
                }

    // let buildOdd (domain: AestasChatDomain) (cache: AestasMessage arrList) (ctor: AestasChatDomain*AestasMessage -> 'msg) =
    //     let sb = StringBuilder()
    //     let result = arrList<'msg>()
    //     cache |> ArrList.fold (fun isSelf msg ->
    //         let isSelf' = msg.sender.uid = domain.Self.uid
    //         if isSelf = isSelf' then
    //             sb.Append msg.Content |> ignore
    //         false
    //         ) false |> ignore