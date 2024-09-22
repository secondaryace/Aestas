//! csproj ref/Lagrange.Core/Lagrange.Core/Lagrange.Core.csproj
//! nuget StbImageSharp=2.27.14
namespace Aestas.Adapters
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Linq
open System.Reflection
open System
open Lagrange.Core
open Lagrange.Core.Common
open Lagrange.Core.Common.Interface
open Lagrange.Core.Common.Interface.Api
open Lagrange.Core.Event
open Lagrange.Core.Message
open Lagrange.Core.Message.Entity
open Lagrange.Core.Utility
open StbImageSharp
open Aestas
open Aestas.Core
open Prim
open Logger
open System.Net.Http
open AutoInit

module rec AestasLagrangeBot =
    let parseLagrangeFriend (friend: Entity.BotFriend) = {
        uid = friend.Uin
        name = if friend.Remarks |> String.IsNullOrEmpty then friend.Nickname else friend.Remarks
    }
    let parseLagrangeMember (member': Entity.BotGroupMember) = {
        uid = member'.Uin
        name = if member'.MemberCard |> String.IsNullOrEmpty then member'.MemberName else member'.MemberCard
    }
    type LagrangeChatGroup(bot: BotContext, private': bool, gid: uint) as this =
        inherit AestasChatDomain()
        let messages = LagrangeMessageCollection this
        let self = {name = bot.BotName; uid = bot.BotUin}
        let cachedName = 
            if private' then (bot.FetchFriends().Result |> Seq.find (fun (f: Entity.BotFriend) -> f.Uin = gid)).Nickname
            else (bot.FetchGroups().Result |> Seq.find (fun (f: Entity.BotGroup) -> f.GroupUin = gid)).GroupName
        let cachedMembers =
            if private' then [|
                    self
                    bot.FetchFriends().Result|> Seq.find (fun (f: Entity.BotFriend) -> f.Uin = gid) |> parseLagrangeFriend
                |]
            else bot.FetchMembers(gid).Result.ToArray() |> Array.filter (fun m -> m.Uin <> self.uid) |> Array.map parseLagrangeMember
        member this.ParseLagrangeEntity (entity: IMessageEntity) =
            match entity with
            | :? TextEntity as t -> AestasText t.Text
            | :? ImageEntity as i -> AestasImage (i.ImageUrl |> bytesFromUrl, "image/jpeg", int i.PictureSize.X, int i.PictureSize.Y)
            | :? RecordEntity as a -> AestasAudio (a.AudioUrl |> bytesFromUrl, "audio/amr", a.AudioLength)
            | :? VideoEntity as v -> AestasVideo (v.VideoUrl |> bytesFromUrl, "") 
            | :? MentionEntity as m -> 
                AestasMention {uid = m.Uin; name = m.Name.Substring(1)}
            | :? ForwardEntity as f ->
                match messages 
                    |> ArrList.tryFindBack (fun m -> (m :?> LagrangeMessage).Sequence = f.Sequence) with
                | Some m -> AestasQuote m.MessageId
                | None -> AestasText "#[quote: not found]"
            | _ -> AestasText "not supported"
        member this.ParseLagrangeMessage (entities: IMessageEntity list) =
            match entities with
            | (:? MarketfaceEntity)::(:? TextEntity as t)::_ -> [AestasText $"#[sticker:{t.Text.Trim('[', ']')}]"]
            | _ -> entities |> List.map this.ParseLagrangeEntity
        // when gets sequence like voice::text, text will be ignored, so we parse it to [voice]::[text]
        member this.ParseAestasContents (contents: AestasContent list) (acc: IMessageEntity list list) =
            match contents, acc with
            | [], acc -> acc
            | AestasBlank::r, _ ->  this.ParseAestasContents r acc
            | AestasAudio (bs, mime, duration)::r, _ ->
                // make the next entity do not follow record 
                this.ParseAestasContents r ([]::[new RecordEntity(bs, duration)]::acc)
            | AestasVideo (bs, mime)::r, _ -> 
                this.ParseAestasContents r ([]::[new VideoEntity(bs)]::acc)
            | AestasText t::r, [] ->
                this.ParseAestasContents r [[new TextEntity(t)]]
            | AestasText t::r, head::tail ->
                this.ParseAestasContents r ((new TextEntity(t)::head)::tail)
            | AestasImage (bs, mime, w, h)::r, [] ->
                this.ParseAestasContents r [[new ImageEntity(bs)]]
            | AestasImage (bs, mime, w, h)::r, head::tail -> 
                this.ParseAestasContents r ((new ImageEntity(bs)::head)::tail)
            | AestasMention m::r, [] ->
                this.ParseAestasContents r [[new MentionEntity("@"+(cachedMembers 
                    |> Array.find (fun m' -> m'.uid = m.uid)).name, m.uid)]] 
            | AestasMention m::r, head::tail -> 
                let at =
                    new MentionEntity("@"+(cachedMembers 
                        |> Array.find (fun m' -> m'.uid = m.uid)).name, m.uid)
                this.ParseAestasContents r ((at::head)::tail)
            | _ -> this.ParseAestasContents contents ((new TextEntity("not supported")::acc.Head)::acc.Tail)
        member val MessageCacheQueue = List<(IMessageAdapter -> unit)*uint32>() with get, set
        
        override this.SendFile data fileName =
            async {
                let chain = (if private' then MessageBuilder.Friend(gid) else MessageBuilder.Group(gid)).Build()
                chain.Add(new FileEntity(data, fileName))
                let! m = bot.SendMessage chain |> Async.AwaitTask
                return Ok ()
            }
        override this.Send callback contents = 
            async {
                let msgs = this.ParseAestasContents contents []
                let chains = 
                    msgs |> List.choose (function
                    | [] -> None
                    | x ->
                        let chain = (if private' then MessageBuilder.Friend(gid) else MessageBuilder.Group(gid)).Build()
                        x |> List.rev |> chain.AddRange
                        Some chain)
                    |> List.rev
                match chains with
                | [] -> 
                    logInfo[this] "Empty message, ignored."
                    return Ok ()
                | [chain] ->
                    // return value is not useful in private chat
                    let! m = bot.SendMessage chain |> Async.AwaitTask
                    if private' then
                        let ret = (messages, chain, true) |> LagrangeMessage
                        callback ret
                        return Ok ()
                    else 
                        this.MessageCacheQueue.Add(callback, m.Sequence.Value)
                        return Ok ()
                | chain::rest ->
                    // return value is not useful in private chat
                    let! m = bot.SendMessage chain |> Async.AwaitTask
                    if private' then
                        let ret = (messages, chain, true) |> LagrangeMessage
                        callback ret
                    else 
                        this.MessageCacheQueue.Add(callback, m.Sequence.Value)
                    for chain in rest do
                        do! Async.Sleep 300
                        do! bot.SendMessage chain |> Async.AwaitTask |> Async.Ignore
                    return Ok ()
            }
        override this.Recall messageId =
            async {
                if private' then return false else
                match 
                    (messages :> arrList<IMessageAdapter>) 
                    |> ArrList.tryFindIndexBack (fun x -> x.MessageId = messageId) 
                with
                | Some i -> 
                    match! 
                        bot.RecallGroupMessage (messages[i] :?> LagrangeMessage).Chain 
                        |> Async.AwaitTask 
                    with
                    | true ->
                        messages.RemoveAt i
                        return true
                    | false -> return false
                | None -> return false
            }
        override _.Self = self
        override _.Virtual = {name = "Virtual"; uid = UInt32.MaxValue}
        override _.Name = cachedName
        override _.Messages = messages
        override _.Members = cachedMembers
        override val Bot = None with get, set
        override _.Private = private'
        override _.DomainId = gid
        override this.OnReceiveMessage msg =
            base.OnReceiveMessage msg
    type LagrangeMessageCollection(domain: LagrangeChatGroup) =
        inherit arrList<IMessageAdapter>()
        member this.MsgList = this
        member _.Domain = domain
        interface IMessageAdapterCollection with
            member this.GetReverseIndex with get (_, i) = this.Count-i-1
            member this.Parse() = this.ToArray() |> Array.map (fun x -> x.Parse())
            member _.Domain = domain
        end
    type LagrangeMessage(collection: LagrangeMessageCollection, messageChain: MessageChain, self) =
        let cachedSender = 
            if self then collection.Domain.Self
            elif messageChain.GroupUin.HasValue then parseLagrangeMember messageChain.GroupMemberInfo
            else parseLagrangeFriend messageChain.FriendInfo
        member _.Sequence = messageChain.Sequence
        member _.Chain = messageChain
        interface IMessageAdapter with
            member _.MessageId = messageChain.MessageId
            member _.SenderId = cachedSender.uid
            member this.Parse() = {
                sender = cachedSender
                contents = messageChain |> List.ofSeq |> collection.Domain.ParseLagrangeMessage
                mid = messageChain.MessageId
                }
            member this.Mention uid = 
                messageChain |> Seq.exists (fun entity -> 
                    match entity with
                    | :? MentionEntity as m when m.Uin = uid -> true
                    | _ -> false
                )
            member _.Collection = collection
            member this.TryGetCommand prefixs = 
                let mutable cmdCache = ""
                prefixs 
                |> Seq.tryFind (fun prefix ->
                    if messageChain.GroupUin.HasValue then
                        if messageChain.Count < 2 then false else
                        match messageChain[0], messageChain[1] with
                        | :? MentionEntity as m, (:? TextEntity as t) ->
                            let t = t.Text.TrimStart()
                            if m.Uin = collection.Domain.Self.uid && t.StartsWith prefix then cmdCache <- t; true
                            else false
                        | _ -> false
                    elif messageChain.Count = 0 then false
                    else
                        match messageChain[0] with
                        | :? TextEntity as t when t.Text.StartsWith prefix -> cmdCache <- t.Text; true
                        | _ -> false)
                |> Option.map (fun prefix -> prefix, cmdCache.Substring(prefix.Length))
            member this.Preview = messageChain.ToPreviewText()
            member this.ParseAsPlainText() = {
                sender = 
                    if messageChain.GroupUin.HasValue then parseLagrangeMember messageChain.GroupMemberInfo
                    else parseLagrangeFriend messageChain.FriendInfo
                contents = [messageChain.ToPreviewText() |> AestasText]
                mid = messageChain.MessageId
                }
        end
    type Config = {id: string; passwordMD5: string;}
    type LagrangeAdapter() as this =
        let keyStore =
            try
            Directory.CreateDirectory("temp/lagrange") |> ignore
            use file = File.OpenRead("temp/lagrange/keystore.json")
            use reader = new StreamReader(file)
            jsonDeserialize<BotKeystore>(reader.ReadToEnd())
            with _ -> new BotKeystore()
        let deviceInfo =
            try
            use file = File.OpenRead("temp/lagrange/deviceinfo.json")
            use reader = new StreamReader(file)
            jsonDeserialize<BotDeviceInfo>(reader.ReadToEnd())
            with _ -> 
                let d = BotDeviceInfo.GenerateInfo()
                d.DeviceName <- "Aestas@Lagrange-" + randomString 6
                d.SystemKernel <- Environment.OSVersion.VersionString
                d.KernelVersion <- Environment.OSVersion.Version.ToString()
                File.WriteAllText("temp/lagrange/deviceinfo.json", jsonSerialize(d))
                d
        let bot = BotFactory.Create(
            let c = new BotConfig() in
            c.UseIPv6Network <- false
            c.GetOptimumServer <- true
            c.AutoReconnect <- true
            c.Protocol <- Protocols.Linux
            c;
            , deviceInfo, keyStore)
        let saveKeyStore keyStore =
            File.WriteAllText("temp/lagrange/keystore.json", jsonSerialize(keyStore))
        let login () =
            logInfo[this] "Try login.."
            if keyStore.Uid |> String.IsNullOrEmpty then
                let qrCode = bot.FetchQrCode() |> await
                if qrCode.HasValue then
                    let struct(url, data) = qrCode.Value
                    let image = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha)
                    // 1px' = 3px, padding = 12px
                    let a = (image.Width)/3 
                    let color x y = Prim.colorAt image.Data image.Width (x*3) (y*3)
                    let cl2ch struct(r, g, b, a) =
                        if r = 0uy then "  "
                        else "■■"
                    for i = 0 to (a-1) do
                        for j = 0 to (a-1) do
                            color i j |> cl2ch |> printf "%s"
                        printf "\n"
                    printfn "LagrangeAdapter: Use this QR Code to login, or use this url else: %s" url
                    bot.LoginByQrCode().Wait()
                else
                    failwith "Fetch QR Code failed"
            else if bot.LoginByPassword() |> await |> not then failwith "Login failed"
        let privateChats = Dictionary<AestasBot, Dictionary<uint32, LagrangeChatGroup>>()
        let groupChats = Dictionary<AestasBot, Dictionary<uint32, LagrangeChatGroup>>()
        let mutable groupsCache: struct(string*uint32*bool)[] option = None
        interface IDisposable with
            member _.Dispose() = bot.Dispose()
        interface IProtocolAdapter with
            member _.Init() =
                (fun context (event: EventArg.BotLogEvent) -> 
                    match event.Level with
                    | EventArg.LogLevel.Debug -> logTrace[this] event.EventMessage
                    | EventArg.LogLevel.Exception -> logError[this] event.EventMessage
                    | EventArg.LogLevel.Information -> logInfo[this] event.EventMessage
                    | EventArg.LogLevel.Warning -> logWarn[this] event.EventMessage
                    | EventArg.LogLevel.Fatal -> logFatal[this] event.EventMessage
                    | EventArg.LogLevel.Verbose -> logDebug[this] event.EventMessage
                    | _ -> ()
                ) |> bot.Invoker.add_OnBotLogEvent
                (fun context event -> 
                    logInfo[this] "Login Succeeded."
                    bot.UpdateKeystore() |> saveKeyStore
                    //bot.FetchCustomFace().Result |> printfn "%A"
                ) |> bot.Invoker.add_OnBotOnlineEvent
                (fun context event -> 
                    logInfo[this] "Captcha!"
                ) |> bot.Invoker.add_OnBotCaptchaEvent
                (fun _ (event: EventArg.FriendMessageEvent) -> 
                    privateChats
                    |> Seq.iter (fun pair -> 
                        if pair.Value.ContainsKey(event.Chain.FriendUin) then
                            let chat = pair.Value[event.Chain.FriendUin]
                            let message = 
                                LagrangeMessage(chat.Messages :?> LagrangeMessageCollection, event.Chain, false)
                            chat.OnReceiveMessage message |> Async.RunSynchronously |> ignore// notice: shouldnt ignore
                    )
                ) |> bot.Invoker.add_OnFriendMessageReceived
                (fun _ (event: EventArg.GroupMessageEvent) -> 
                    groupChats
                    |> Seq.iter (fun pair -> 
                        if pair.Value.ContainsKey(event.Chain.GroupUin.Value) then
                            let chat = pair.Value[event.Chain.GroupUin.Value]
                            if event.Chain.FriendUin = bot.BotUin then
                                match
                                    chat.MessageCacheQueue |> ArrList.tryFindBack (fun (callback, seq) -> 
                                        seq = event.Chain.Sequence)
                                with
                                | Some (callback, seq) -> 
                                    chat.MessageCacheQueue.Remove(callback, seq) |> ignore
                                    LagrangeMessage(chat.Messages :?> LagrangeMessageCollection, event.Chain, true) |> callback
                                | None -> ()
                            else
                                let message = 
                                    LagrangeMessage(chat.Messages :?> LagrangeMessageCollection, event.Chain, false)
                                chat.OnReceiveMessage message |> Async.RunSynchronously |> ignore// notice: shouldnt ignore
                    )
                ) |> bot.Invoker.add_OnGroupMessageReceived
                (fun _ (event: EventArg.FriendPokeEvent) -> 
                    privateChats
                    |> Seq.iter (fun pair -> 
                        if pair.Value.ContainsKey(event.OperatorUin) then
                            let chat = pair.Value[event.OperatorUin]
                            match chat.Bot with
                            | None -> ()
                            | Some bot ->
                                [AestasText "(poke you)"] |> Some |> bot.SelfTalk chat |> Async.RunSynchronously |> ignore// notice: shouldnt ignore
                    )
                ) |> bot.Invoker.add_OnFriendPokeEvent
                (fun _ (event: EventArg.GroupPokeEvent) -> 
                    privateChats
                    |> Seq.iter (fun pair -> 
                        if pair.Value.ContainsKey(event.GroupUin) then
                            let chat = pair.Value[event.GroupUin]
                            match chat.Bot with
                            | None -> ()
                            | Some bot ->
                                [AestasText "(poke you)"] |> Some |> bot.SelfTalk chat |> Async.RunSynchronously |> ignore// notice: shouldnt ignore
                    )
                ) |> bot.Invoker.add_OnGroupPokeEvent
            member _.FetchDomains() = 
                let privates = 
                    bot.FetchFriends(true).Result
                    |> ArrList.map (fun f -> struct(f.Nickname, f.Uin))
                let groups = 
                    bot.FetchGroups(true).Result
                    |> ArrList.map (fun f -> struct(f.GroupName, f.GroupUin))
                let result =
                    Array.init (privates.Count+groups.Count) (fun i -> 
                        if i < privates.Count then 
                            let struct(name, uin) = privates[i]
                            struct(name, uin, true)
                        else 
                            let struct(name, uin) = groups[i-privates.Count]
                            struct(name, uin, false)
                    )
                groupsCache <- Some result; result
            member this.InitDomainView abot domainId = 
                let groups = 
                    match groupsCache with
                    | Some g -> g
                    | None -> (this :> IProtocolAdapter).FetchDomains()
                let idid = 
                    groups
                    |> Array.tryFind (fun struct(_, id, _) -> id = domainId)
                match idid with
                | None -> failwith $"Domain {domainId} not found"
                | Some idid ->
                    let struct(name, id, private') = idid
                    if private' then
                        if privateChats.ContainsKey abot |> not then privateChats.Add(abot, Dictionary())
                        let chat = 
                            if privateChats[abot].ContainsKey id then privateChats[abot][id]
                            else 
                                let g = LagrangeChatGroup(bot, true, id)
                                privateChats[abot].Add(id, g); g
                        chat
                    else
                        if groupChats.ContainsKey abot |> not then groupChats.Add(abot, Dictionary())
                        let chat = 
                            if groupChats[abot].ContainsKey id then groupChats[abot][id]
                            else 
                                let g = LagrangeChatGroup(bot, false, id)
                                groupChats[abot].Add(id, g); g
                        chat
            member _.Run() = async {
                login()
            }
        interface IAutoInit<IProtocolAdapter, unit> with
            static member Init _ = 
                let lagrange = new LagrangeAdapter() :> IProtocolAdapter
                lagrange.Init()
                lagrange.Run() |> Async.RunSynchronously
                lagrange