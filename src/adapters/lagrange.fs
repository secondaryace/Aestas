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
    // fix the s**t sequence problem
    type LagrangeQQSequenceFix =
    | SequenceOk
    | SequenceIll
    | SequenceReturned of uint32
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
            | :? RecordEntity as a -> AestasAudio (a.AudioUrl |> bytesFromUrl, "audio/amr")
            | :? VideoEntity as v -> AestasVideo (v.VideoUrl |> bytesFromUrl, "") 
            | :? MentionEntity as m -> 
                AestasMention {uid = m.Uin; name = m.Name.Substring(1)}
            | :? ForwardEntity as f ->
                logInfo[0] $"ForwardEntity: {f.MessageId}"
                match messages 
                    |> ArrList.tryFindBack (fun m -> (m :?> LagrangeMessage).Sequence = f.Sequence) with
                | Some m -> AestasText "#[quote not found]"//m.Parse() |> AestasQuote
                | None -> AestasText "#[quote not found]"
            | _ -> AestasText "not supported"
        member this.ParseAestasContent (content: AestasContent): IMessageEntity =
            match content with
            | AestasText t -> new TextEntity(t)
            | AestasImage (bs, mime, w, h) -> new ImageEntity(bs)
            | AestasAudio (bs, mime) -> new RecordEntity(bs)
            | AestasVideo (bs, mime) -> new VideoEntity(bs)
            | AestasMention m -> 
                new MentionEntity("@"+(cachedMembers 
                    |> Array.find (fun m' -> m'.uid = m.uid)).name, m.uid)
            | AestasMappingContent c -> c.Convert this.Bot.Value this |> this.ParseAestasContent
            | _ -> TextEntity "not supported"
        member val MessageCacheQueue = List<(IMessageAdapter -> unit)*uint32>() with get, set
        override this.Send callback msgs = 
            async {
                let msgs = msgs |> List.map this.ParseAestasContent
                let chain = (if private' then MessageBuilder.Friend(gid) else MessageBuilder.Group(gid)).Build()
                chain.AddRange msgs
                // return value is not useful in private chat
                let! m = bot.SendMessage chain |> Async.AwaitTask
                if private' then
                    let ret = (messages, chain, SequenceIll) |> LagrangeMessage
                    // s**t way to fix s**t thing
                    // performance not good, comment it
                    // async {
                    //     Threading.Thread.Sleep 1500
                    //     let! smsgs = bot.GetRoamMessage(chain, 5u) |> Async.AwaitTask
                    //     let smsg =
                    //         smsgs |> ArrList.tryFindBack (fun m -> 
                    //             if m.FriendUin = self.uid then
                    //                 if m.Count = chain.Count then
                    //                     if m.ToPreviewText() = chain.ToPreviewText() then true else false
                    //                 else false
                    //             else false)
                    //     let tmsgi = 
                    //         (messages :> arrList<IMessageAdapter>) 
                    //         |> ArrList.tryFindIndexBack (fun m -> m = ret)
                    //     match smsg, tmsgi with
                    //     | Some m, Some i ->
                    //         messages[i] <- (messages, m, SequenceOk) |> LagrangeMessage :> IMessageAdapter
                    //     | _ -> ()
                    // } |> Async.Start
                    callback ret
                    return Ok ()
                else 
                    this.MessageCacheQueue.Add(callback, m.Sequence.Value)
                    return Ok ()
            }
        override this.Recall msg =
            async {
                if private' then return false else
                match 
                        (messages :> arrList<IMessageAdapter>) 
                        |> ArrList.tryFindIndexBack (fun x -> x.MessageId = msg.mid) 
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
    type LagrangeMessage(collection: LagrangeMessageCollection, messageChain: MessageChain, seqFix: LagrangeQQSequenceFix) =
        let cachedSender = 
            if messageChain.GroupUin.HasValue then parseLagrangeMember messageChain.GroupMemberInfo
            else parseLagrangeFriend messageChain.FriendInfo
        member _.Sequence = messageChain.Sequence
        member _.Chain = messageChain
        member _.SeqFix = seqFix
        interface IMessageAdapter with
            member _.MessageId = messageChain.MessageId
            member _.SenderId = cachedSender.uid
            member this.Parse() = {
                sender = cachedSender
                content = messageChain |> List.ofSeq |> List.map collection.Domain.ParseLagrangeEntity
                mid = messageChain.MessageId
                }
            member this.Mention uid = 
                messageChain |> Seq.exists (fun entity -> 
                    match entity with
                    | :? MentionEntity as m when m.Uin = uid -> true
                    | _ -> false
                )
            member _.Collection = collection
            member this.Command = 
                if messageChain.GroupUin.HasValue then
                    if messageChain.Count < 2 then None else
                    match messageChain[0], messageChain[1] with
                    | :? MentionEntity as m, (:? TextEntity as t) ->
                        let t = t.Text.TrimStart()
                        if m.Uin = collection.Domain.Self.uid && t.StartsWith '#'  then t.Substring 1 |> Some
                        else None
                    | _ -> None
                elif messageChain.Count = 0 then None 
                else
                    match messageChain[0] with
                    | :? TextEntity as t when t.Text.StartsWith '#' -> t.Text.Substring 1 |> Some
                    | _ -> None
            member this.Preview = messageChain.ToPreviewText()
            member this.ParseAsPlainText() = {
                sender = 
                    if messageChain.GroupUin.HasValue then parseLagrangeMember messageChain.GroupMemberInfo
                    else parseLagrangeFriend messageChain.FriendInfo
                content = [messageChain.ToPreviewText() |> AestasText]
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
                d.DeviceName <- "Aestas@Lagrange-" + Prim.randomString 6
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
                                LagrangeMessage(chat.Messages :?> LagrangeMessageCollection, event.Chain, SequenceOk)
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
                                        if seq = event.Chain.Sequence then 
                                            callback (LagrangeMessage(chat.Messages :?> LagrangeMessageCollection, event.Chain, SequenceOk))
                                            true
                                        else false
                                    )
                                with
                                | Some (callback, seq) -> 
                                    chat.MessageCacheQueue.Remove(callback, seq) |> ignore
                                    LagrangeMessage(chat.Messages :?> LagrangeMessageCollection, event.Chain, SequenceOk) |> callback
                                | None -> ()
                            else
                                let message = 
                                    LagrangeMessage(chat.Messages :?> LagrangeMessageCollection, event.Chain, SequenceOk)
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
                                bot.SelfTalk chat [AestasText "(poke you)"] |> Async.RunSynchronously |> ignore// notice: shouldnt ignore
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
                                bot.SelfTalk chat [AestasText "(poke you)"] |> Async.RunSynchronously |> ignore// notice: shouldnt ignore
                    )
                ) |> bot.Invoker.add_OnGroupPokeEvent
            member _.FetchDomains() = 
                let privates = 
                    bot.FetchFriends().Result
                    |> ArrList.map (fun f -> struct(f.Nickname, f.Uin))
                let groups = 
                    bot.FetchGroups().Result
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
            member this.InitDomainView(abot, domainId) = 
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
        interface IAutoInit<IProtocolAdapter*string, unit> with
            static member Init _ = 
                let lagrange = new LagrangeAdapter() :> IProtocolAdapter
                lagrange.Init()
                lagrange.Run() |> Async.RunSynchronously
                lagrange, "lagrange"



            
    // type Config = {id: string; passwordMD5: string;}
    // let run () =
    //     let keyStore =
    //         try
    //         use file = File.OpenRead("keystore.json")
    //         use reader = new StreamReader(file)
    //         jsonDeserialize<BotKeystore>(reader.ReadToEnd())
    //         with _ -> new BotKeystore()
    //     let deviceInfo =
    //         try
    //         use file = File.OpenRead("deviceinfo.json")
    //         use reader = new StreamReader(file)
    //         jsonDeserialize<BotDeviceInfo>(reader.ReadToEnd())
    //         with _ -> 
    //             let d = BotDeviceInfo.GenerateInfo()
    //             d.DeviceName <- "Aestas@Lagrange-" + Prim.randomString 6
    //             d.SystemKernel <- Environment.OSVersion.VersionString
    //             d.KernelVersion <- Environment.OSVersion.Version.ToString()
    //             File.WriteAllText("deviceinfo.json", jsonSerialize(d))
    //             d
    //     use bot = BotFactory.Create(
    //         let c = new BotConfig() in
    //         c.UseIPv6Network <- false
    //         c.GetOptimumServer <- true
    //         c.AutoReconnect <- true
    //         c.Protocol <- Protocols.Linux
    //         c;
    //         , deviceInfo, keyStore)
    //     let notes = new Notes()
    //     let preChat (notes: Notes) (model: IChatClient) =
    //         if model.DataBase.Count = 0 then model.DataBase.Add(Dictionary())
    //         if model.DataBase[0].ContainsKey("time") |> not then model.DataBase[0].Add("time", "")
    //         if model.DataBase[0].ContainsKey("notebook") |> not then model.DataBase[0].Add("notebook", "")
    //         model.DataBase[0]["time"] <- DateTime.Now.ToString()
    //         model.DataBase[0]["notebook"] <-
    //             let sb = StringBuilder()
    //             sb.Append '[' |> ignore
    //             for note in notes do
    //                 sb.Append(note).Append(',') |> ignore
    //             sb.Append ']' |> ignore
    //             sb.ToString()
    //     let aestas = {
    //         privateChats = Dictionary()
    //         groupChats = Dictionary()
    //         prePrivateChat = preChat notes
    //         preGroupChat = preChat notes
    //         postPrivateChat = (fun _ -> ())
    //         postGroupChat = (fun _ -> ())
    //         media = {
    //             image2text = 
    //                 try
    //                 let _itt = FuyuImageClient("profiles/chat_info_fuyu.json")
    //                 (fun x -> _itt.Receive("introduce the image", x)) |> Some
    //                 with _ -> None
    //             text2speech = 
    //                 try
    //                 let _tts = MsTTS_Client("profiles/ms_tts.json")
    //                 (fun x y -> _tts.Receive(x, y)) |> Some
    //                 with _ -> None
    //             text2image = None
    //             speech2text = None
    //             stickers = loadStickers()
    //         }
    //         privateCommands = getCommands(fun a -> 
    //             a.Domain &&& AestasCommandDomain.Private <> AestasCommandDomain.None)
    //         groupCommands = getCommands(fun a -> 
    //             a.Domain &&& AestasCommandDomain.Group <> AestasCommandDomain.None)
    //         awakeMe = loadAwakeMe()
    //         notes = notes
    //     }
    //     (fun context event -> 
    //         printfn "%A" event
    //     ) |> bot.Invoker.add_OnBotLogEvent
    //     (fun context event -> 
    //         printfn "Login Succeeded."
    //         bot.UpdateKeystore() |> saveKeyStore
    //         //bot.FetchCustomFace().Result |> printfn "%A"
    //     ) |> bot.Invoker.add_OnBotOnlineEvent
    //     (fun context event -> 
    //         printfn "Captcha! %A" event
    //     ) |> bot.Invoker.add_OnBotCaptchaEvent
    //     privateChat aestas |> bot.Invoker.add_OnFriendMessageReceived
    //     groupChat aestas |> bot.Invoker.add_OnGroupMessageReceived
    //     privatePoke aestas (Dictionary<uint32, DateTime>()) |> bot.Invoker.add_OnFriendPokeEvent
    //     login keyStore bot
    //     Console.ReadLine() |> ignore
    // let saveKeyStore keyStore =
    //     File.WriteAllText("keystore.json", jsonSerialize(keyStore))
    // let login keyStore bot =
    //     printfn "Try login.."
    //     if keyStore.Uid |> String.IsNullOrEmpty then
    //         let qrCode = bot.FetchQrCode() |> await
    //         if qrCode.HasValue then
    //             let struct(url, data) = qrCode.Value
    //             let image = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha)
    //             // 1px' = 3px, padding = 12px
    //             let a = (image.Width)/3 
    //             let color x y = Prim.colorAt image.Data image.Width (x*3) (y*3)
    //             let cl2ch struct(r, g, b, a) =
    //                 if r = 0uy then "  "
    //                 else "■■"
    //             for i = 0 to (a-1) do
    //                 for j = 0 to (a-1) do
    //                     color i j |> cl2ch |> printf "%s"
    //                 printf "\n"
    //             printfn "Use this QR Code to login, or use this url else: %s" url
    //             bot.LoginByQrCode().Wait()
    //         else
    //             failwith "Fetch QR Code failed"
    //     else if bot.LoginByPassword() |> await |> not then failwith "Login failed"
    // let privateChat aestas context event =
    //     async {
    //     let print s =
    //         MessageBuilder.Friend(event.Chain.FriendUin).Add(new TextEntity(s)).Build()
    //         |> context.SendMessage |> await |> ignore
    //     if event.Chain.FriendUin = context.BotUin then () else
    //     if tryProcessCommand aestas context event.Chain print true event.Chain.FriendUin then () else
    //     let name = 
    //         if event.Chain.FriendInfo.Remarks |> String.IsNullOrEmpty then
    //             event.Chain.FriendInfo.Nickname
    //         else event.Chain.FriendInfo.Remarks
    //     let dialog = event.Chain |> MessageParser.parseElements true (getMsgPrivte context event.Chain aestas true) aestas.media
    //     try
    //     if aestas.privateChats.ContainsKey(event.Chain.FriendUin) |> not then 
    //         aestas.privateChats.Add(event.Chain.FriendUin, 
    //         IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; Ernie_35P|])
    //     let chat = aestas.privateChats[event.Chain.FriendUin]
    //     aestas.prePrivateChat chat
    //     chat.Turn $"{{{name}}} {dialog}" (buildElement context aestas.notes aestas.media event.Chain.FriendUin true)
    //     aestas.postPrivateChat chat
    //     with e -> printfn "Error: %A" e
    //     } |> Async.Start
    // let privatePoke aestas pokeTimeStamp context event =
    //     async {
    //     if pokeTimeStamp.ContainsKey context.BotUin then () 
    //     else pokeTimeStamp.Add(context.BotUin, DateTime(0L))
    //     if event.EventTime - pokeTimeStamp[context.BotUin] < TimeSpan(0, 0, 1) then () else
    //     printfn "poke from %d, at %A" event.FriendUin event.EventTime
    //     pokeTimeStamp[context.BotUin] <- event.EventTime
    //     if event.FriendUin = context.BotUin then () else
    //     let dialog = "(poke you)"
    //     try
    //     if aestas.privateChats.ContainsKey(event.FriendUin) |> not then 
    //         aestas.privateChats.Add(event.FriendUin, 
    //         IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; Ernie_35P|])
    //     let chat = aestas.privateChats[event.FriendUin]
    //     chat.Turn dialog (buildElement context aestas.notes aestas.media event.FriendUin true)
    //     with e -> printfn "Error: %A" e
    //     } |> Async.Start
    // let groupChat aestas context event =
    //     async {
    //     let print s =
    //         MessageBuilder.Group(event.Chain.GroupUin.Value).Add(new TextEntity(s)).Build()
    //         |> context.SendMessage |> await |> ignore
    //     if event.Chain.FriendUin = context.BotUin then () else
    //     let atMe =
    //         (fun (a: IMessageEntity) -> 
    //             match a with 
    //             | :? MentionEntity as m ->
    //                 m.Uin = context.BotUin
    //             | _ -> false
    //         ) |> event.Chain.Any
    //     let awakeMe = 
    //         let msg = event.Chain.FirstOrDefault(fun t -> t :?TextEntity) in
    //         if isNull msg then false else
    //         let text = (msg :?> TextEntity).Text in 
    //             aestas.awakeMe 
    //             |> Seq.tryFind (fun p -> 
    //                 if p.Key.StartsWith("regex:") then Regex.IsMatch(text, p.Key[6..])
    //                 else text.Contains(p.Key))
    //             |> function 
    //                 | Some p -> p.Value > Random.Shared.NextSingle()
    //                 | None -> false
    //     let name = 
    //         if event.Chain.GroupMemberInfo.MemberCard |> String.IsNullOrEmpty then
    //             event.Chain.GroupMemberInfo.MemberName
    //         else event.Chain.GroupMemberInfo.MemberCard
    //     if atMe || awakeMe then 
    //         if atMe && tryProcessCommand aestas context event.Chain print false event.Chain.GroupUin.Value then () else
    //         let dialog = event.Chain |> MessageParser.parseElements true (getMsgGroup context event.Chain aestas true) aestas.media
    //         try
    //         if aestas.groupChats.ContainsKey(event.Chain.GroupUin.Value) |> not then 
    //             aestas.groupChats.Add(event.Chain.GroupUin.Value, 
    //             (IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; Ernie_35P|], arrList()))
    //         let chat, cache = aestas.groupChats[event.Chain.GroupUin.Value]
    //         let dialog = 
    //             if cache.Count = 0 then $"{{{name}}} {dialog}" else
    //             let sb = StringBuilder()
    //             for sender, msg in cache do
    //                 sb.Append('{').Append(sender).Append('}') |> ignore
    //                 sb.Append(' ').Append(msg) |> ignore
    //                 sb.Append(";\n") |> ignore
    //             cache.Clear()
    //             sb.Append('{').Append(name).Append('}') |> ignore
    //             sb.Append(' ').Append(dialog).ToString()
    //         aestas.preGroupChat chat
    //         chat.Turn dialog (buildElement context aestas.notes aestas.media event.Chain.GroupUin.Value false)
    //         aestas.postGroupChat chat
    //         with e -> printfn "Error: %A" e
    //     else
    //         try
    //         if aestas.groupChats.ContainsKey(event.Chain.GroupUin.Value) |> not then 
    //             aestas.groupChats.Add(event.Chain.GroupUin.Value, 
    //             (IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; Ernie_35P|], arrList()))
    //         let dialog = event.Chain |> MessageParser.parseElements false (getMsgGroup context event.Chain aestas false) aestas.media
    //         let _, cache = aestas.groupChats[event.Chain.GroupUin.Value]
    //         cache.Add (name, dialog)
    //         with e -> printfn "Error: %A" e
    //     } |> Async.Start
    
    // let rec getMsgPrivte context chain aestas flag u =
    //         let msgs = context.GetRoamMessage(chain, 30u).Result
    //         let f = msgs.FirstOrDefault(fun a -> a.Sequence = u)
    //         if isNull f then "Message not found, maybe too far."
    //         else $"{{{chain.FriendInfo.Nickname}}} {f |> MessageParser.parseElements flag (getMsgPrivte context chain aestas flag) aestas.media}"
    // let rec getMsgGroup context chain aestas flag u =
    //         let msgs = context.GetGroupMessage(chain.GroupUin.Value, chain.Sequence-30u, chain.Sequence).Result
    //         let f = msgs.FirstOrDefault(fun a -> a.Sequence = u)
    //         if isNull f then "Message not found, maybe too far."
    //         else
    //             let name = 
    //                 if f.GroupMemberInfo.MemberCard |> String.IsNullOrEmpty then
    //                     f.GroupMemberInfo.MemberName
    //                 else f.GroupMemberInfo.MemberCard
    //             $"{{{name}}} {f |> MessageParser.parseElements flag (getMsgGroup context chain aestas flag) aestas.media}"
    // let buildElement context notes media id isPrivate (s: string) =
    //     async {
    //     let es = MessageParser.parseBotOut notes media s
    //     let newContent() = if isPrivate then MessageBuilder.Friend(id) else MessageBuilder.Group(id)
    //     let mutable content = newContent()
    //     let send (content: MessageBuilder) =
    //         if content.Build().Count <> 0 then
    //             let result = content.Build() |> context.SendMessage |> await
    //             printfn "sends:%d" result.Result
    //     for e in es do
    //         match e with
    //         | :? VideoEntity
    //         | :? ForwardEntity
    //         | :? RecordEntity ->
    //             send content
    //             content <- newContent()
    //             e |> newContent().Add |> send
    //         // | :? MarketFaceEntity as m ->
    //         //     send content
    //         //     content <- newContent()
    //         //     newContent().Add(e).Add(new TextEntity(m.Summary)) |> send
    //         | _ -> content.Add e |> ignore
    //     send content
    //     }
    // let tryProcessCommand aestas context msgs print isPrivate id = 
    //     let msg = msgs.FirstOrDefault(fun t -> t :? TextEntity)
    //     if isNull msg then false else
    //     let text = (msg :?> TextEntity).Text.Trim()
    //     printfn "%s" text
    //     if text.StartsWith '#' |> not then false else
    //     let source = text[1..]
    //     try
    //     match source with
    //     | x when x.StartsWith '#' ->
    //         let command = source.ToLower().Split(' ')
    //         match command[0] with
    //         | "#help" ->
    //             print "Commands: help, current, ernie, gemini, cohere, dumpcontext"
    //         | "#current" ->
    //             let tp = 
    //                 if isPrivate then 
    //                     if aestas.privateChats.ContainsKey id then 
    //                         aestas.privateChats[id].GetType().Name
    //                     else "UnitClient"
    //                 else 
    //                     if aestas.groupChats.ContainsKey id then 
    //                         let chat, _ = aestas.groupChats[id] in chat.GetType().Name
    //                     else "UnitClient"
    //             print $"Model is {tp}"
    //         | "#ernie" ->
    //             if command.Length < 2 then print "Usage: ernie [model=chara|35|40|35p|40p]"
    //             else
    //                 let model = 
    //                     match command[1] with
    //                     | "chara" -> Ernie_Chara
    //                     | "35" -> Ernie_35
    //                     | "40" -> Ernie_40
    //                     | "35p" -> Ernie_35P
    //                     | "40p" -> Ernie_40P
    //                     | _ -> 
    //                         command[1] <- "default:chara"
    //                         Ernie_Chara
    //                 if isPrivate then aestas.privateChats[id] <- IChatClient.Create<ErnieClient> [|"profiles/chat_info_private_ernie.json"; model|]
    //                 else aestas.groupChats[id] <- IChatClient.Create<ErnieClient> [|"profiles/chat_info_group_ernie.json"; model|], arrList()
    //                 print $"Model changed to ernie{command[1]}"
    //         | "#gemini" -> 
    //             if command.Length < 2 then print "Usage: gemini [model=15|10]"
    //             else
    //                 let profile = if isPrivate then "profiles/chat_info_private_gemini.json" else "profiles/chat_info_group_gemini.json"
    //                 let model = 
    //                     match command[1] with
    //                     | "15" -> IChatClient.Create<GeminiClient> [|profile; false|]
    //                     | "10" -> IChatClient.Create<Gemini10Client> [|profile; ""|]
    //                     | "vespera" -> IChatClient.Create<Gemini10Client> [|profile; "vespera-k7ejxi4vj84j"|]
    //                     | _ -> 
    //                         command[1] <- "default:10"
    //                         IChatClient.Create<Gemini10Client> [|profile; ""|]
    //                 if isPrivate then aestas.privateChats[id] <- model
    //                 else aestas.groupChats[id] <- model, arrList()
    //                 print $"Model changed to gemini {command[1]}"
    //         | "#cohere" -> 
    //             let model = IChatClient.Create<CohereClient> [|if isPrivate then "profiles/chat_info_private_cohere.json" else "profiles/chat_info_group_cohere.json"|]
    //             if isPrivate then aestas.privateChats[id] <- model
    //             else aestas.groupChats[id] <- model, arrList()
    //             print $"Model changed to cohere"
    //         | "#dumpcontext" ->
    //             let chat = if isPrivate then aestas.privateChats[id] else let chat, _ = aestas.groupChats[id] in chat
    //             let sb = StringBuilder()
    //             let msgs = chat.Messages
    //             let length = 
    //                 if command.Length < 2 then msgs.Count
    //                 else try command[1] |> Int32.Parse with | _ -> msgs.Count
    //             for i = 0 to length-1 do
    //                 let m = msgs[msgs.Count-length+i]
    //                 sb.Append($"{m.role}: {m.content}\n") |> ignore
    //             print (sb.ToString())
    //         | _ -> 
    //             print "Unknown command."
    //     | _ when isPrivate ->
    //         let model =
    //             if aestas.privateChats.ContainsKey id then ref aestas.privateChats[id]
    //             else let x = UnitClient() in aestas.privateChats.Add(id, x); ref x
    //         {aestas = aestas; context = context; chain = msgs; commands = aestas.privateCommands; log = print; model = model}
    //         |> excecute <| source
    //         aestas.privateChats[id] <- model.Value
    //     | _ ->
    //         let model =
    //             if aestas.groupChats.ContainsKey id then aestas.groupChats[id] |> fst |> ref
    //             else let x = UnitClient() in aestas.groupChats.Add(id, (x, arrList())); ref x
    //         {aestas = aestas; context = context; chain = msgs; commands = aestas.groupCommands; log = print; model = model}
    //         |> excecute <| source
    //         aestas.groupChats[id] <- model.Value, if aestas.groupChats[id] |> fst = model.Value then snd aestas.groupChats[id] else arrList()
    //     true
    //     with | ex -> print $"Error: {ex}"; true
    
    // type _Stickers = {
    //     from_file: Dictionary<string, string>
    //     from_url: Dictionary<string, string>
    //     from_market: Dictionary<string, _MarketSticker>
    // }
    // let loadStickers() =
    //     let result = Dictionary<string, Sticker>()
    //     try
    //     let json = 
    //         File.ReadAllText("profiles/stickers.json") |> jsonDeserialize<_Stickers>
    //     for p in json.from_file do
    //         let data = File.ReadAllBytes p.Value
    //         result.Add(p.Key, data |> ImageSticker)
    //     for s in json.from_market do
    //         result.Add(s.Key, s.Value |> MarketSticker)
    //     with _ -> ()
    //     result

    // let loadAwakeMe() =
    //     try
    //     File.ReadAllText("profiles/awake_me.json") |> jsonDeserialize<Dictionary<string, float32>>
    //     with
    //     | _ -> Dictionary()