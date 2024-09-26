module rec Aestas.Core
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Linq
open System.Reflection
open System
open Prim

module Logger =
    type LogLevel =
        | Trace = 0
        | Debug = 1
        | Info = 2
        | Warn = 3
        | Error = 4
        | Fatal = 5
    let inline levelToColor lv = 
        match lv with
        | LogLevel.Trace -> ConsoleColor.DarkGray
        | LogLevel.Debug -> ConsoleColor.Gray
        | LogLevel.Info -> ConsoleColor.White
        | LogLevel.Warn -> ConsoleColor.Yellow
        | LogLevel.Error -> ConsoleColor.Red
        | LogLevel.Fatal -> ConsoleColor.DarkRed
        | _ -> ConsoleColor.White
    [<Struct>]
    type LogEntry = {time: DateTime; level: LogLevel; message: string}
        with
            member this.Print() = $"[{this.time:``yyyy-MM-dd HH:mm:ss``}] [{this.level.ToString().ToUpper()}] {this.message}"
                
    let internal logs = Dictionary<obj, LogEntry arrList>()
    let onLogUpdate = arrList<LogEntry -> unit>()
    let getLoggerOwners() = logs.Keys |> Seq.toArray
    let getLogs o = logs[o] :> IReadOnlyList<LogEntry>
    let inline internal _log (key: obj) (lv: LogLevel) (s: string) =
        if logs.ContainsKey key |> not then logs.Add(key, arrList())
        let log = {time = DateTime.Now; level = lv; message = s}
        logs[key].Add log
        if logs[key].Count > 100 then
            lock logs[key] (fun() -> 
                Directory.CreateDirectory("logs") |> ignore
                use write = new StreamWriter($"logs/{key}.log", true, Encoding.UTF8)
                logs[key] |> ArrList.iter (fun x -> x.Print() |> write.WriteLine)
                logs[key].Clear()
            )
        onLogUpdate |> ArrList.iter (fun f -> f log)
    let inline internal __log (lv: LogLevel) (key: obj) (s: string) = _log key lv s
    let log = IndexerBox<obj, LogLevel -> string -> unit> _log
    let logTrace = IndexerBox<obj, string -> unit> (__log LogLevel.Trace)
    let logDebug = IndexerBox<obj, string -> unit> (__log LogLevel.Debug)
    let logInfo = IndexerBox<obj, string -> unit> (__log LogLevel.Info)
    let logWarn = IndexerBox<obj, string -> unit> (__log LogLevel.Warn)
    let logError = IndexerBox<obj, string -> unit> (__log LogLevel.Error)
    let logFatal =  IndexerBox<obj, string -> unit> (__log LogLevel.Fatal)
    let inline internal _logf (key: obj) (lv: LogLevel) (fmt: Printf.StringFormat<'t, unit>) = Printf.kprintf (_log key lv) fmt
    let inline internal __logf (lv: LogLevel) (key: obj) (fmt: Printf.StringFormat<'t, unit>) = Printf.kprintf (_log key lv) fmt
    let logf<'t> = IndexerBox<obj, LogLevel -> Printf.StringFormat<'t, unit> -> 't> _logf
    let logTracef<'t> = IndexerBox<obj, Printf.StringFormat<'t, unit> -> 't> (__logf LogLevel.Trace)
    let logDebugf<'t> = IndexerBox<obj, Printf.StringFormat<'t, unit> -> 't> (__logf LogLevel.Debug)
    let logInfof<'t> = IndexerBox<obj, Printf.StringFormat<'t, unit> -> 't> (__logf LogLevel.Info)
    let logWarnf<'t> = IndexerBox<obj, Printf.StringFormat<'t, unit> -> 't> (__logf LogLevel.Warn)
    let logErrorf<'t> = IndexerBox<obj, Printf.StringFormat<'t, unit> -> 't> (__logf LogLevel.Error)
    let logFatalf<'t> = IndexerBox<obj, Printf.StringFormat<'t, unit> -> 't> (__logf LogLevel.Fatal)
    
type IMessageAdapter =
    abstract member MessageId: uint64 with get
    abstract member SenderId: uint32 with get
    abstract member Parse: unit -> AestasMessage
    abstract member Mention: targetMemberId: uint32 -> bool 
    abstract member TryGetCommand: string seq -> (string * string) option
    abstract member Collection: IMessageAdapterCollection with get
    abstract member Preview: string with get
    abstract member ParseAsPlainText: unit -> AestasMessage
type IMessageAdapterCollection =
    inherit ICollection<IMessageAdapter>
    inherit IList<IMessageAdapter>
    abstract member GetReverseIndex: int*int -> int with get
    abstract member Parse: unit -> AestasMessage[]
    abstract member Domain: AestasChatDomain with get
type IProtocolAdapter = 
    abstract member Init: unit -> unit
    abstract member Run: unit -> Async<unit>
    /// return: name * domainId * isPrivate
    abstract member FetchDomains: unit -> struct(string*uint32*bool)[]
    abstract member InitDomainView: bot: AestasBot -> domainId: uint32 -> AestasChatDomain
// Use abstract class rather than interface to provide default implementation
[<AbstractClass>]
type AestasChatDomain() =
    abstract member Bind: AestasBot -> unit
    abstract member UnBind: AestasBot -> unit
    abstract member Messages: IMessageAdapterCollection with get
    abstract member Private: bool with get
    abstract member DomainId: uint32 with get
    abstract member Self: AestasChatMember with get
    abstract member Virtual: AestasChatMember with get
    abstract member Members: AestasChatMember[] with get
    abstract member Bot: AestasBot option with get, set
    abstract member Send: callback: (IMessageAdapter -> unit) -> contents: AestasContent list -> Async<Result<unit, string>>
    abstract member Recall: messageId: uint64 -> Async<bool>
    abstract member Name: string with get
    abstract member OnReceiveMessage: IMessageAdapter -> Async<Result<unit, string>>
    abstract member MakeFoldMessage: AestasContent list list -> IMessageAdapterCollection
    default this.Bind bot = 
        this.Bot <- Some bot
    default this.UnBind bot =
        this.Bot <- None
    default this.OnReceiveMessage msg = 
        async {
            match this.Bot with
            | None -> return Error "No bot binded to this domain"
            | Some bot -> 
                match! bot.Reply this msg with
                | Error e, _ -> return Error e
                | Ok [], _ -> 
                    this.Messages.Add msg
                    return Ok ()
                | Ok reply, callback -> 
                    return! reply |> this.Send callback
                    // match! reply |> this.Send with
                    // | Error e -> return Error e
                    // | Ok rmsg ->
                    //     this.Messages.Add msg
                    //     this.Messages.Add rmsg
                    //     rmsg.Parse() |> callback
                    //     return Ok msg

        }
    default this.Recall messageId =
        async {
            return false
        }
    default this.MakeFoldMessage contents =
        raise <| NotImplementedException()
type AestasChatDomains = Dictionary<uint32, AestasChatDomain>
type BotContentLoadStrategy =
    /// Load anything as plain text
    | StrategyLoadNone
    /// Only load media from messages that mentioned the bot or private messages
    | StrategyLoadOnlyMentionedOrPrivate
    /// Load all media from messages
    | StrategyLoadAll
    /// Load media from messages that satisfy the predicate
    | StrategyLoadByPredicate of (IMessageAdapter -> bool)
type BotFriendStrategy =
    | StrategyFriendNone
    | StrategyFriendAll
    | StrategyFriendWhitelist of Dictionary<uint32, Set<uint32>>
    | StrategyFriendBlacklist of Dictionary<uint32, Set<uint32>>
type BotMessageReplyStrategy = 
    /// Won't reply to any message
    | StrategyReplyNone
    /// Only reply to messages that mentioned the bot or private messages
    | StrategyReplyOnlyMentionedOrPrivate
    /// Reply to all messages
    | StrategyReplyAll
    /// Reply to messages that satisfy the predicate
    | StrategyReplyByPredicate of (AestasMessage -> bool)
type BotMessageCacheStrategy =
    /// Clear cache after reply immediately
    | StrategyCacheNone
    /// Only cache messages that mentioned the bot or private messages
    | StrategyCacheOnlyMentionedOrPrivate
    /// Cache all messages
    | StrategyCacheAll
type BotContentParseStrategy =
    /// Parse all content to plain text
    | StrategyParseAllToPlainText
    /// Parse all content to AestasContent, ignore errors
    | StrategyParseAndIgnoreError
    /// Parse all content to AestasContent, alert errors like <Couldn't find function [{funcName}]>
    | StrategyParseAndAlertError
    /// Parse all content to AestasContent, restore content to original if error occurs
    | StrategyParseAndRestoreError
type BotContextStrategy =
    /// Trim context when exceed length
    | StrategyContextTrimWhenExceedLength of int
    /// Compress context when exceed length
    | StrategyContextCompressWhenExceedLength of int
    /// Reserve all context
    | StrategyContextReserveAll
/// interest is a function, ∈ [0, 100]
type BotInterestCurve =
    /// Bot will always interest in this domain
    | CurveInterestAlways
    /// Bot will lose interest in this domain after certain time
    | CurveInterestTruncateAfterTime of int
    /// Use your own function to determine interest
    | CurveInterestFunction of (int<sec> -> int)
/// Used in Model <-> Aestas.Core <-> Protocol
/// Provide extra content type. For example, market faces in QQ
type IProtocolSpecifyContent =
    /// .NET type of this content
    abstract member Type: Type with get
    abstract member ToPlainText: unit -> string
    /// Convert this to something that only protocol can understand
    abstract member Convert: AestasBot -> AestasChatDomain -> obj option
[<Struct>]
type AestasChatMember = {uid: uint32; name: string}
type AestasContent = 
    | AestasBlank
    | AestasText of string 
    /// byte array, mime type, width, height
    | AestasImage of {|data: byte[]; mimeType: string; width: int; height: int|}
    /// byte array, mime type, duration in seconds
    | AestasAudio of {|data: byte[]; mimeType: string; duration: int|}
    | AestasVideo of {|data: byte[]; mimeType: string; duration: int|}
    | AestasMention of AestasChatMember
    | AestasQuote of uint64
    | AestasFold of IMessageAdapterCollection
    /// byte array, mime type, file name
    | AestasFile of {|data: byte[]; mimeType: string; fileName: string|}
    | ProtocolContent of {|funcName: string; param: (string * string) list; content: string|}
[<Struct>]
type AestasMessage = {
    sender: AestasChatMember
    contents: AestasContent list
    mid: uint64
    }
type InputConverterFunc = IMessageAdapterCollection -> AestasContent -> string
/// domain * params * content
type 't ContentCtor = AestasBot -> AestasChatDomain -> (string * string) list -> string -> 't
type OverridePluginFunc = AestasContent ContentCtor
type ContentParser = Result<AestasContent, string> ContentCtor
type SystemInstructionBuilder = AestasBot -> StringBuilder -> unit
type PrefixBuilder = AestasBot -> AestasMessage -> AestasMessage
/// Bot class used in Aestas Core
/// Create this directly
type AestasBot() =
    let groups = AestasChatDomains()
    let extraData = Dictionary<string, obj>()
    let commandExecuters = Dictionary<string, CommandExecuter>()
    let mutable model: ILanguageModelClient option = None
    let mutable originalSystemInstruction = ""
    /// Bot name, Default is "AestasBot"
    member val Name = "AestasBot" with get, set
    member this.Model
        with get() = model
        and set value = Option.iter (fun x -> this.BindModel x) value

    member this.Domain
        with get domainId = groups[domainId]
        and set domainId (value: AestasChatDomain) =
            if value.DomainId = domainId then this.BindDomain value
            else failwith "Domain ID mismatch"
    member this.Domains = groups :> IReadOnlyDictionary<uint32, AestasChatDomain> 
    member val ModelInputConverter = Builtin.modelInputConverters with get, set
    member val ModelOutputParser = Builtin.modelOutputParser with get, set
    member val FriendStrategy = StrategyFriendAll with get, set
    member val ContentLoadStrategy = StrategyLoadAll with get, set
    member val MessageReplyStrategy = StrategyReplyNone with get, set
    member val MessageCacheStrategy = StrategyCacheAll with get, set
    member val ContentParseStrategy = StrategyParseAndAlertError with get, set
    member val ContextStrategy = StrategyContextReserveAll with get, set
    member val MemberCommandPrivilege: Dictionary<uint32, CommandPrivilege> = Dictionary() with get
    member val ContentParsers: Dictionary<string, struct(ContentParser*(AestasBot -> StringBuilder -> unit))> = Dictionary() with get
    member val ProtocolContents: Dictionary<uint32, Dictionary<string, (AestasBot -> StringBuilder -> unit)>> = Dictionary() with get
    member val SystemInstructionBuilder: PipeLineChain<AestasBot*StringBuilder> option = None with get, set
    member val PrefixBuilder: PrefixBuilder option = None with get, set
    member _.ExtraData = extraData
    member _.TryGetExtraData key =
        if extraData.ContainsKey key then Some extraData[key] else None
    member this.AddExtraData (key: string) (value: obj) = 
        if extraData.ContainsKey key then failwith "Key already exists"
        else extraData.Add(key, value)
    member _.CommandExecuters = commandExecuters.AsReadOnly()
    member _.TryGetCommandExecuter key = 
        if commandExecuters.ContainsKey key then Some commandExecuters[key] else None
    member this.AddCommandExecuter key value =
        if value.GetType().BaseType.GetGenericTypeDefinition() <> typedefof<CommandExecuter<_>> 
        then failwith "Not a command executer" 
        else
            if commandExecuters.ContainsKey key then failwith "Key already exists"
            else commandExecuters.Add(key, value)
    member this.RemoveCommandExecuter key =
        if commandExecuters.Remove key then Ok ()
        else Error "Key not found"
    member this.SystemInstruction 
        with get() = 
            match this.SystemInstructionBuilder with
            | Some b -> 
                let sb = new StringBuilder(originalSystemInstruction)
                (b.Invoke(this, sb) |> snd).ToString()
            | None -> originalSystemInstruction
        and set value = originalSystemInstruction <- value
    member this.IsFriend (domain: AestasChatDomain) uid =
        match this.FriendStrategy with
        | StrategyFriendAll -> true
        | StrategyFriendNone -> false
        | StrategyFriendWhitelist w 
            when w.ContainsKey domain.DomainId && w[domain.DomainId].Contains uid -> true
        | StrategyFriendWhitelist _ -> false
        | StrategyFriendBlacklist b 
            when b.ContainsKey domain.DomainId && b[domain.DomainId].Contains uid -> false
        | _ -> true
    member this.BindDomain (domain: AestasChatDomain) = 
        if groups.ContainsKey domain.DomainId then
            groups[domain.DomainId].UnBind this
            groups[domain.DomainId] <- domain
        else groups.Add(domain.DomainId, domain)
        domain.Bind this
    member this.BindModel (model': ILanguageModelClient) = 
        model |> Option.iter (fun x -> x.UnBind this) 
        model <- Some model'
        model'.Bind this
    member this.CheckContextLength (domain: AestasChatDomain) =
        async {
            match this.ContextStrategy, this.Model with
            | StrategyContextTrimWhenExceedLength l, Some m when domain.Messages.Count >= l -> 
                let rec go i =
                    if i = 0 then () else
                    m.RemoveCache domain domain.Messages[0].MessageId
                    domain.Messages.RemoveAt 0
                    go (i-1)
                go (domain.Messages.Count - l / 2)
            | StrategyContextTrimWhenExceedLength l, None when domain.Messages.Count >= l -> 
                let rec go i =
                    if i = 0 then () else
                    domain.Messages.RemoveAt 0
                    go (i-1)
                go (domain.Messages.Count - l / 2)
            | StrategyContextCompressWhenExceedLength l, Some m when domain.Messages.Count >= l -> 
                m.CacheContents this domain [AestasText $"**Please summarize all the above content to compress the chat history. Retain key memories, and delete the rest.**
Format:
## [Topic of the conversation]:

[Summary of the conversation, including all details you consider important.]"]
                match! m.GetReply this domain with
                | Ok rmsg, _ -> 
                    let s = 
                        rmsg 
                        |> List.choose (fun x -> 
                            match x with
                            | AestasText s -> Some s
                            | _ -> None
                        ) |> String.concat ""
                    this.ClearCachedContext domain
                    Logger.logInfo[0] $"Get compressed message: {s}"
                    m.CacheContents this domain [AestasText s]
                | _ -> this.ClearCachedContext domain
            | StrategyContextCompressWhenExceedLength l, None when domain.Messages.Count >= l -> 
                domain.Messages.Clear()
            | _ -> ()
        }
    member this.ParseIMessageAdapter (domain: AestasChatDomain) (significant: bool) (message: IMessageAdapter) =
        let message = 
            match this.ContentLoadStrategy with
            | StrategyLoadNone -> message.ParseAsPlainText()
            | StrategyLoadAll ->  message.Parse()
            | StrategyLoadByPredicate p when p message -> message.Parse()
            | StrategyLoadOnlyMentionedOrPrivate when message.Mention domain.Self.uid || domain.Private || significant -> message.Parse()
            | _ -> message.ParseAsPlainText()
        match this.PrefixBuilder with
        | Some build -> build this message
        | None -> message
    member this.Reply (domain: AestasChatDomain) (message: IMessageAdapter) =
        async {
            do! this.CheckContextLength domain
            match this.Model, message.TryGetCommand this.CommandExecuters.Keys with
            | _ when groups.ContainsValue domain |> not -> return Error "This domain hasn't been added to this bot", ignore
            | _ when message.SenderId |> this.IsFriend domain |> not -> return Ok [], ignore
            | _ when message.SenderId = domain.Self.uid -> return Ok [], ignore
            | _, Some(prefix, command)  ->
                this.CommandExecuters[prefix].Execute {
                    bot = this
                    domain = domain
                    log = AestasText >> List.singleton >> domain.Send ignore >> Async.Ignore >> Async.Start
                    privilege = 
                        if this.MemberCommandPrivilege.ContainsKey message.SenderId then
                            this.MemberCommandPrivilege[message.SenderId]
                        else CommandPrivilege.Normal
                    } command
                return Ok [], ignore
            | None, _ -> return Error "No model binded to this bot", ignore
            | Some model, _ ->
                // check if illegal strategy
                match this.MessageReplyStrategy, this.MessageCacheStrategy with
                | StrategyReplyAll, StrategyCacheNone ->
                    failwith "Check your strategy, you can't reply to message without any cache"
                | _ -> ()
                let message' = this.ParseIMessageAdapter domain false message
                let callback' =
                    let callback' (callback: AestasMessage -> unit) rmsg = 
                        domain.Messages.Add rmsg
                        rmsg.Parse() |> callback
                    match this.MessageCacheStrategy with
                    | StrategyCacheNone -> 
                        model.CacheMessage this domain message'
                        domain.Messages.Add message
                        fun c r -> callback' c r; model.ClearCache domain
                    | StrategyCacheAll -> 
                        model.CacheMessage this domain message'
                        domain.Messages.Add message
                        callback'
                    | StrategyCacheOnlyMentionedOrPrivate when message.Mention domain.Self.uid || domain.Private ->
                        model.CacheMessage this domain message'
                        domain.Messages.Add message
                        callback'
                    | _ ->
                        callback'
                match this.MessageReplyStrategy with
                | StrategyReplyOnlyMentionedOrPrivate when message.Mention domain.Self.uid || domain.Private -> 
                    let! result, callback = model.GetReply this domain
                    return result, callback' callback
                | StrategyReplyAll ->
                    let! result, callback = model.GetReply this domain
                    return result, callback' callback
                | StrategyReplyByPredicate p when p message' ->
                    let! result, callback = model.GetReply this domain
                    return result, callback' callback
                | _ -> 
                    return Ok [], ignore
        }
    member this.SelfTalk (domain: AestasChatDomain) (content: AestasContent list option) = 
        async {
            match this.Model with
            | _ when groups.ContainsValue domain |> not -> return Error "This domain hasn't been added to this bot"
            | None -> return Error "No model binded to this bot"
            | Some model ->
                match content with
                | None -> ()
                | Some content -> model.CacheContents this domain content
                match! model.GetReply this domain with
                | Error e, _ -> return Error e
                | Ok rmsg, callback -> 
                    return! rmsg |> domain.Send (fun rmsg ->
                        domain.Messages.Add rmsg
                        rmsg.Parse() |> callback
                    )
        }
    member this.Recall (domain: AestasChatDomain) (messageId: uint64) = 
        async {
            let messageInd = domain.Messages |> IList.tryFindIndexBack (fun x -> x.MessageId = messageId)
            match messageInd with
            | None -> return Error "Message not found"
            | Some messageInd ->
                if domain.Messages[messageInd].SenderId <> domain.Self.uid then return Error "Not my message" else
                match! domain.Recall messageId with
                | true ->
                    Logger.logWarn[0] $"{this.Name} could not recall message {messageId} in domain {domain.Name}"
                | false -> ()
                match this.Model with
                | _ when groups.ContainsValue domain |> not -> return Error "This domain hasn't been added to this bot"
                | None -> return Ok ()
                | Some model -> 
                    model.RemoveCache domain messageId
                    return Ok ()
        }
    member this.ClearCachedContext(domain: AestasChatDomain) = 
        match this.Model with
        | None -> ()
        | Some model -> model.ClearCache domain
        domain.Messages.Clear()
        GC.Collect()
type GenerationConfig = {
    /// factor to control the randomness of the generation
    temperature: float option
    /// maximum length of the generated text, in tokens
    maxLength: int option
    /// number of highest probability tokens to keep for top-k sampling
    topK: int option
    /// cumulative probability threshold for nucleus sampling, not supported by all models
    topP: float option
    /// frequency penalty to reduce the probability of repeating the same token, not supported by all models
    frequencyPenalty: float option
    /// when arives string in this list, the model will stop generating
    stop: string list option
    }
let defaultGenerationConfig = {
    temperature = Some 0.95
    maxLength = Some 1024
    topK = Some 64
    topP = None
    frequencyPenalty = None
    stop = None
    }
let noneGenerationConfig = {
    temperature = None
    maxLength = None
    topK = None
    topP = None
    frequencyPenalty = None
    stop = None
    }
type CacheMessageCallback = AestasMessage -> unit
type ILanguageModelClient =
    abstract member Bind: bot : AestasBot -> unit
    abstract member UnBind: bot : AestasBot -> unit
    /// bot -> domain -> message -> response * callback
    abstract member GetReply: bot: AestasBot -> domain: AestasChatDomain -> Async<Result<AestasContent list, string>*CacheMessageCallback>
    /// bot * domain * message -> unit, with a certain sender in AestasMessage, dont send the message
    abstract member CacheMessage: bot: AestasBot -> domain: AestasChatDomain -> message: AestasMessage -> unit
    /// bot * domain * contents -> unit, with no sender, dont send the message
    abstract member CacheContents: bot: AestasBot -> domain: AestasChatDomain -> contents: (AestasContent list) -> unit
    abstract member ClearCache: AestasChatDomain -> unit
    abstract member RemoveCache: domain: AestasChatDomain -> messageId: uint64 -> unit
type CommandAccessibleDomain = 
    | None = 0 
    | Private = 1 
    | Group = 2 
    | All = 3
type CommandPrivilege = 
    | BelowNormal = 0
    | Normal = 1
    | High = 2
    | AboveHigh = 3
type CommandEnvironment = {
    bot: AestasBot
    domain: AestasChatDomain
    log: string -> unit
    privilege: CommandPrivilege
    }
[<AbstractClass>]
type CommandExecuter() =
    abstract member Execute: CommandEnvironment -> string -> unit
[<AbstractClass>]
type CommandExecuter<'t>(commands: 't seq) =
    inherit CommandExecuter()
    abstract member AddCommand: 't -> unit
    abstract member Commands: arrList<'t>
    default _.Commands = arrList commands
    default this.AddCommand command = this.Commands.Add command
type TextToImageArgument ={
    prompt: string
    negative: string
    resolution: int*int
    seed: int option
    }
type UnitMessageAdapter(message: AestasMessage, collection: UnitMessageAdapterCollection) =
    interface IMessageAdapter with
        member _.Mention uid = message.contents |> List.exists (fun x -> 
            match x with
            | AestasMention m -> m.uid = uid
            | _ -> false)
        member _.Parse() = message
        member _.ParseAsPlainText() = {
            contents = message.contents 
            |> List.map (function | AestasText s -> s | _ -> "") 
            |> String.concat "" |> AestasText |> List.singleton
            mid = message.mid
            sender = message.sender
            }   
        member _.Collection = collection
        member _.TryGetCommand prefixs =
            prefixs
            |> Seq.tryFind (fun prefix ->
                match message.contents with
                | AestasText s::_ when s.StartsWith prefix -> 
                    true
                | _ -> false)
            |> Option.map (fun prefix ->
                match message.contents with
                | AestasText s::_ -> 
                    prefix, s.Substring(prefix.Length)
                | _ -> prefix, "")
        member _.MessageId = message.mid
        member _.Preview = message.contents |> List.map (function | AestasText s -> s | _ -> "") |> String.concat ""
        member _.SenderId = message.sender.uid
type UnitMessageAdapterCollection(domain: VirtualDomain) =
    inherit arrList<IMessageAdapter>()
    interface IMessageAdapterCollection with
        member this.GetReverseIndex with get (_, i) = this.Count-i-1    
        member this.Parse() = this.ToArray() |> Array.map (fun x -> x.Parse())
        member _.Domain = domain
    end                 
type VirtualDomain(send, recall, bot, user, domainId, domainName, isPrivate) as this=
    inherit AestasChatDomain()
    let members = [user; bot] |> arrList
    let messages = UnitMessageAdapterCollection this
    let mutable mid = 0UL
    override _.Private = isPrivate
    override _.DomainId = domainId
    override _.Self = bot
    override _.Virtual = {name = "Virtual"; uid = UInt32.MaxValue}
    override _.Name = domainName
    override _.Messages = messages
    override _.Members = members.ToArray()
    override val Bot = None with get, set
    override _.Send callback contents =
        async {
            UnitMessageAdapter({sender = bot; contents = contents; mid = mid}, messages) |> callback
            send mid contents
            mid <- mid + 1UL
            return Ok ()
        }
    override _.Recall messageId =
        async {
            match messages |> ArrList.tryFindIndexBack (fun x -> x.MessageId = messageId) with
            | None -> return false
            | Some i -> 
                recall messageId
                messages.RemoveAt i
                return true
        }
    member this.Input contents =
        async {
            let message = {sender = user; contents = contents; mid = mid}
            mid <- mid + 1UL
            match! UnitMessageAdapter(message, messages) |> this.OnReceiveMessage with
            | Ok () -> return Ok (), mid - 1UL
            | Error e -> return Error e, mid - 1UL
        }
type UnitClient() =
    interface ILanguageModelClient with
        member _.Bind bot = ()
        member _.UnBind bot = ()
        member _.GetReply bot domain  = 
            async {
                return Ok [], ignore
            }
        member _.CacheMessage bot domain message = ()
        member _.CacheContents bot domain contents = ()
        member _.ClearCache domain = ()
        member _.RemoveCache domain messageID = ()
    end
module ConsoleBot =
    type ConsoleDomain(botName, user) as this =
        inherit AestasChatDomain()
        let messages = ConsoleMessageCollection(this)
        let cachedContext = arrList<string>()
        let self = {name = botName; uid = 1u}
        override this.Send callback msgs = 
            async {
                let sb = StringBuilder()
                msgs |> List.iter (function
                | AestasText x -> sb.Append x |> ignore
                | AestasImage _ -> sb.Append "[image: not supported]" |> ignore
                | AestasAudio _ -> sb.Append "[audio: not supported]" |> ignore
                | AestasVideo _ -> sb.Append "[video: not supported]" |> ignore
                | AestasMention x -> sb.Append $"@{x.name}" |> ignore
                | AestasQuote x -> sb.Append $"[quote: not implemented]" |> ignore
                | AestasFold x -> sb.Append $"[foldedMessage: not implemented]" |> ignore
                | _ -> ())
                let s = sb.ToString()
                cachedContext.Add $"{self.name}: {s}"
                ConsoleMessage(messages, self, s) |> callback
                return Ok ()
            }
        override this.Recall messageId = 
            async { 
                match 
                    (messages :> arrList<IMessageAdapter>) 
                    |> ArrList.tryFindIndexBack (fun x -> x.MessageId = messageId) 
                with
                | Some i -> 
                    messages.RemoveAt i
                    return true
                | None -> return false
            }
        override _.Private = true
        override _.DomainId = 0u
        override _.Self = self
        override _.Virtual = {name = "Virtual"; uid = UInt32.MaxValue}
        override _.Name = "AestasConsole"
        override _.Messages = messages
        override _.Members = [|user; self|]
        override val Bot = None with get, set
        member val InnerMid = 0UL with get, set
        member _.CachedContext = cachedContext

    type ConsoleMessageCollection(consoleDomain: ConsoleDomain) as this =
        inherit arrList<IMessageAdapter>()
        let messages = this :> arrList<IMessageAdapter>
        member _.Domain = consoleDomain
        member val MsgList = messages
        interface IMessageAdapterCollection with
            member _.GetReverseIndex with get (_, i) = messages.Count-i-1    
            member _.Parse() = messages.ToArray() |> Array.map (fun x -> x.Parse())
            member _.Domain = consoleDomain
        end
    type ConsoleMessage(collection: ConsoleMessageCollection, sender: AestasChatMember, msg: string) =
        member _.Sender = sender
        interface IMessageAdapter with
            member val MessageId = collection.Domain.InnerMid <- collection.Domain.InnerMid+1UL; collection.Domain.InnerMid
            member _.SenderId = sender.uid
            member _.Mention _ = false
            member this.Parse() = {sender = sender; contents = [AestasText msg]; mid = (this :> IMessageAdapter).MessageId}
            member _.Collection = collection
            member _.TryGetCommand prefixs =
                prefixs
                |> Seq.tryFind (fun prefix -> msg.StartsWith prefix)
                |> Option.map (fun prefix -> prefix, msg.Substring(prefix.Length))
            member this.Preview = msg
            member this.ParseAsPlainText() = (this :> IMessageAdapter).Parse()
        end
    type ConsoleChat() =
        let consoleUser = {name = "ConsoleInput"; uid = 0u}
        let consoleChats = Dictionary<AestasBot, ConsoleDomain>()
        member val ConsoleHook: unit -> unit = ignore with get, set
        /// only for cli
        member this.Send(bot: AestasBot, msg: string) =
            if consoleChats.ContainsKey bot then
                let consoleChat = consoleChats[bot]
                consoleChat.CachedContext.Add $"You: {msg}"
                let consoleMessage = ConsoleMessage(consoleChat.Messages :?> ConsoleMessageCollection, consoleUser, msg)
                async { 
                    try
                        do! consoleMessage |> consoleChat.OnReceiveMessage |> Async.Ignore
                    with ex -> consoleChat.CachedContext.Add $"Error: {ex.Message}"
                    this.ConsoleHook()
                } |> Async.Start |> ignore
        /// only for cli
        member _.BindedBots = consoleChats.Keys |> arrList
        /// only for cli
        member _.BotContext b = consoleChats[b].CachedContext
        member _.Init() = ()
        member _.Run() = async { () }
        member _.FetchDomains() = [|struct("AestasConsole", 0u, true)|]
        member _.InitDomainView(bot, domainId) = 
            if domainId <> 0u then failwith "No such domain"
            if consoleChats.ContainsKey bot then consoleChats[bot]
            else 
                let consoleChat = ConsoleDomain(bot.Name, consoleUser)
                consoleChats.Add(bot, consoleChat)
                consoleChat
        interface IProtocolAdapter with
            member this.Init() = this.Init()
            member this.Run() = this.Run()
            member this.FetchDomains() = this.FetchDomains()
            member this.InitDomainView bot domainId = this.InitDomainView(bot, domainId)
    let singleton = ConsoleChat()
module Builtin =
    let inline domainToAccessibleDomain (domain: AestasChatDomain) =
        if domain.Private then CommandAccessibleDomain.Private else CommandAccessibleDomain.Group
    let inline toString (o: obj) =
        match o with
        | :? int as i -> i.ToString()
        | :? float as f -> f.ToString()
        | :? single as f -> f.ToString()
        | :? bool as b -> b.ToString()
        | :? char as c -> c.ToString()
        | :? byte as b -> b.ToString()
        | :? sbyte as b -> b.ToString()
        | :? string as s -> s
        | :? Type as t -> t.Name
        | _ -> o.GetType().Name
    let rec modelInputConverters (domain: AestasChatDomain) = function
    | AestasBlank -> ""
    | AestasText x -> x
    | AestasImage _ -> "#[image: not supported]"
    | AestasAudio _ -> "#[audio: not supported]"
    | AestasVideo _ -> "#[video: not supported]"
    | AestasFile _ -> "#[file: not supported]"
    | AestasMention x -> $"#[mention: {x.name}]"
    | AestasQuote x -> $"#[quote: {x}]"
    | AestasFold x -> $"#[foldedMessage: {x.Count} messages]"
    | ProtocolContent _ -> "<should not be here, maybe a bug>"
    let overridePrimCtor = dict' [
        "text", fun (domain: AestasChatDomain, param: (string*string) list, content) -> AestasText content
        "image", fun (domain, param, content) ->
            AestasImage {|
                data = Array.zeroCreate<byte> 0
                mimeType = "image/png"
                width = 0; height = 0
                |}
        "audio", fun (domain, param, content) -> 
            AestasAudio {|
                data = Array.zeroCreate<byte> 0
                mimeType = "audio/mp3"
                duration = 0
                |}
        "video", fun (domain, param, content) -> 
            AestasVideo {|
                data = Array.zeroCreate<byte> 0
                mimeType = "video/mp4"
                duration = 0
                |}
        "mention", fun (domain, param, content) ->
            let content = content.Trim()
            match domain.Members |> Array.tryFind (fun x -> x.name = content) with
            | Some m -> AestasMention {uid = m.uid; name = m.name}
            | None -> AestasText $"[mention: {content}]"
        "blank" , fun (domain, param, content) -> AestasBlank
    ]
    let overridePrimTip = [
        "mention", "Using format #[mention: name] to mention or at someone. e.g. #[mention: Stella]"
        "blank", "Using format #[blank] to indicate a blank content"
    ]
    let modelOutputParser (bot: AestasBot) (domain: AestasChatDomain) (botOut: string) =
        let cache = StringBuilder()
        let result = arrList<AestasContent>()
        let rec scanParam i (r: string*string) =
            match botOut[i] with
            | '@' | ':' | ']' -> i, (fst r, cache.ToString()), cache.Clear() |> ignore
            | '=' -> let s = cache.ToString() in cache.Clear() |> ignore; scanParam (i+1) (s, snd r)
            | _ -> 
                cache.Append(botOut[i]) |> ignore
                scanParam (i+1) r
        // state index bracketLevel funcName param
        // state 0 -> funcName
        // state 1 -> param
        // state 2 -> content
        let rec scanBracket s i v f p =
            match botOut[i] with
            | '@' when s = 0 ->
                let f = if cache.Length <> 0 then cache.ToString() else f
                cache.Clear() |> ignore
                let i, pr, _ = scanParam (i+1) ("", "")
                scanBracket 1 i v f (pr::p)
            | '@' when s = 1 ->
                let i, pr, _ = scanParam (i+1) ("", "")
                scanBracket 1 i v f (pr::p)
            | ':' when s < 2 -> 
                let f = if cache.Length <> 0 then cache.ToString() else f
                cache.Clear() |> ignore
                scanBracket 2 (i+1) v f p
            | ']' when v = 1 && s = 0 -> i+1, cache.ToString(), p, "", cache.Clear() |> ignore
            | ']' when v = 1 -> i+1, f, p, cache.ToString(), cache.Clear() |> ignore
            // nested square brackets
            | '[' -> 
                cache.Append '[' |> ignore
                scanBracket s (i+1) (v+1) f p
            | ']' -> 
                cache.Append ']' |> ignore
                scanBracket s (i+1) (v-1) f p
            | _ ->
                cache.Append botOut[i] |> ignore
                if i+1 = botOut.Length then i+1, f, p, cache.ToString(), cache.Clear() |> ignore 
                else scanBracket s (i+1) v f p
        let rec go i =
            let checkCache() = 
                if cache.Length > 0 then
                    let t = cache.ToString()
                    if t |> String.IsNullOrWhiteSpace |> not then 
                        t |> AestasText |> result.Add
                    cache.Clear() |> ignore
            if i+1 = botOut.Length then
                cache.Append botOut[i] |> ignore
                checkCache()
            elif i = botOut.Length then
                checkCache()
            else
            match botOut[i], botOut[i+1] with
            | '#', '[' ->
                checkCache()
                //#[name@pram=m@pram'=n:content]
                let i, funcName, param, content, _ = scanBracket 0 (i+2) 1 "" []
                let error s =
                    match bot.ContentParseStrategy with
                    | StrategyParseAndAlertError ->
                        s |> AestasText |> result.Add
                    | StrategyParseAndRestoreError ->
                        let sb = StringBuilder()
                        sb.Append("#[").Append(funcName) |> ignore
                        param |> List.iter (fun (p, v) -> sb.Append('@').Append(p).Append('=').Append(v) |> ignore)
                        if content |> String.IsNullOrEmpty |> not then 
                            sb.Append(':').Append(content) |> ignore
                        sb.ToString() |> AestasText |> result.Add
                    | _ -> ()
                //sprintf "funcName: %s, param: %A, content: %s" funcName param content |> Logger.logInfo[0]
                try
                if overridePrimCtor.ContainsKey funcName then
                    let content = overridePrimCtor[funcName] (domain, param, content)
                    result.Add content
                else if bot.ContentParsers.ContainsKey funcName then
                    match fst' bot.ContentParsers[funcName] bot domain param content with
                    | Ok content ->
                        content |> result.Add
                    | Error emsg ->
                        error $"<Error occured when parsing [{funcName}]: {emsg}>"
                else if 
                    bot.ProtocolContents.ContainsKey domain.DomainId
                    && bot.ProtocolContents[domain.DomainId].ContainsKey funcName then
                    ProtocolContent {|
                        funcName = funcName
                        param = param
                        content = content
                        |} |> result.Add
                else
                    error $"<Couldn't find function [{funcName}] with param: {param}, content: {content}>"
                    // log here
                with ex ->
                    error $"<Error occured when parsing [{funcName}]: {ex.Message}>"
                go i
            | _ -> 
                cache.Append botOut[i] |> ignore
                go (i+1)
        match bot.ContentParseStrategy with
        | StrategyParseAllToPlainText -> 
            [botOut |> AestasText]
        | _ ->
            go 0
            result |> List.ofSeq
    let buildSystemInstruction (bot: AestasBot, sb: StringBuilder) =
        let prompt = sb.ToString()
        sb.Clear() |> ignore
        sb.Append "## 1.About yourself\n" |> ignore
        sb.Append prompt |> ignore
        sb.Append "## 2.Some functions for you\n" |> ignore
        overridePrimTip |> List.iter (fun (name, tips) -> 
            sb.Append("**").Append(name).AppendLine("**:").AppendLine(tips) |> ignore)
        bot.ContentParsers |> Dict.iter (fun name struct(ctor, tipBuilder) -> 
            sb.Append("**").Append(name).AppendLine("**:") |> ignore
            tipBuilder bot sb
            sb.AppendLine("") |> ignore)
        bot.ProtocolContents.Values |> Seq.iter (Dict.iter (fun name tipBuilder -> 
            sb.Append("**").Append(name).AppendLine("**:") |> ignore
            tipBuilder bot sb
            sb.AppendLine("") |> ignore))
        bot, sb
    let buildPrefix (bot: AestasBot) (msg: AestasMessage) =
        {
            contents = AestasText $"[{msg.sender.name}|{DateTime.Now:``yyyy-MM-dd HH:mm``}] "::msg.contents
            sender = msg.sender
            mid = msg.mid
        }
    type SpacedTextCommand = {
        name: string
        description: string
        accessibleDomain: CommandAccessibleDomain
        privilege: CommandPrivilege
        execute: SpacedTextCommandExecuter -> CommandEnvironment -> string[] -> unit
    }
    type SpacedTextCommandExecuter(commands') =
        inherit CommandExecuter<SpacedTextCommand>(commands')
        let commands = Dictionary<string, SpacedTextCommand>()
        do
            commands' |> List.iter (fun c -> commands.Add(c.name, c))
        override _.Commands = arrList commands.Values
        override _.AddCommand cmd =
            commands.Add(cmd.name, cmd)
        override this.Execute env cmd = 
            let cmd = Regex.Matches(cmd, """("[^\n\r]+"|[^\n\r ]+)""")
            if cmd.Count = 0 then env.log "No command found"
            else
                let cmd = 
                    cmd |> Array.ofSeq 
                    |> Array.map (fun x -> if x.Value.StartsWith("\"") then x.Value.[1..^1] else x.Value)
                let name = cmd[0]
                let args = cmd |> Array.skip 1
                match Dict.tryGetValue name commands with
                | Some cmd-> 
                    match 
                        cmd.privilege <= env.privilege,
                        domainToAccessibleDomain env.domain &&& cmd.accessibleDomain <> CommandAccessibleDomain.None
                    with
                    | _, false -> env.log $"Command {name} not found"
                    | false, _ -> env.log $"Permission denied"
                    | _ ->
                        try cmd.execute this env args with ex -> env.log $"Error: {ex.Message}"
                | None -> env.log $"Command {name} not found"
    let versionCommand() = {
        name = "version"
        description = "Print the version of Aestas"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env args -> 
            env.log $"Aestas version {version}"
        }
    let clearCommand() = {
        name = "clear"
        description = "Clear the cached context"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env args -> 
            env.bot.ClearCachedContext env.domain
            env.log "Cached context cleared"
        }
    let helpCommand() = {
        name = "help"
        description = "List all commands"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env args ->
            let sb = StringBuilder()
            sb.Append "## Commands" |> ignore
            executer.Commands |>
            ArrList.iter (fun v ->
                if domainToAccessibleDomain env.domain &&& v.accessibleDomain <> CommandAccessibleDomain.None then 
                    sb.Append $"\n* {v.name}:\n   {v.description}" |> ignore)
            sb.ToString() |> env.log
        }
    let echoCommand() = {
        name = "echo"
        description = "Repeat the input. Use --file to redirect the output to a file"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env args ->
            let mode = args.Contains "--file"
            let args = 
                if mode then args |> Array.removeAt (args |> Array.findIndex (( = ) "--file"))
                else args
            let sb = StringBuilder()
            args |> Array.iter (fun x -> sb.AppendLine x |> ignore)
            if sb.Length <> 0 then sb.Remove(sb.Length - 1, 1) |> ignore
            if mode then
                use ms = new MemoryStream()
                use writer = new StreamWriter(ms, Encoding.UTF8)
                writer.Write(sb.ToString())
                writer.Flush()
                AestasFile {|
                    data = ms.ToArray()
                    mimeType = "text/plain"
                    fileName = "echo.txt"
                    |}
                |> List.singleton
                |> env.domain.Send ignore
                 |> Async.Ignore |> Async.Start
            else sb.ToString() |> env.log
        }
    let lsexecCommand() = {
        name = "lsexec"
        description = "List all executers"
        accessibleDomain = CommandAccessibleDomain.Group
        privilege = CommandPrivilege.Normal
        execute = fun executer env args ->
            env.bot.CommandExecuters
            |> Seq.map (fun p -> sprintf "%s: %s" p.Key (p.Value.GetType().Name))
            |> String.concat "\n"
            |> env.log
        }
    let lsfrwlCommand() = {
        name = "lsfrwl"
        description = "List friend whitelist"
        accessibleDomain = CommandAccessibleDomain.Group
        privilege = CommandPrivilege.High
        execute = fun executer env args ->
            match env.bot.FriendStrategy with
            | StrategyFriendWhitelist wl ->
                let sb = StringBuilder()
                sb.AppendLine "## Whitelist" |> ignore
                if wl.ContainsKey env.domain.DomainId then
                    wl[env.domain.DomainId] |> Set.iter (fun x -> 
                        match env.domain.Members |> Array.tryFind (fun y -> y.uid = x) with
                        | Some m -> sb.AppendLine m.name |> ignore
                        | None -> ())
                sb.ToString() |> env.log
            | _ -> env.log "FriendStrategy not supported"
        }
    let lsfrblCommand() = {
        name = "lsfrbl"
        description = "List friend Blacklist"
        accessibleDomain = CommandAccessibleDomain.Group
        privilege = CommandPrivilege.High
        execute = fun executer env args ->
            match env.bot.FriendStrategy with
            | StrategyFriendBlacklist bl ->
                let sb = StringBuilder()
                sb.AppendLine "## Blacklist" |> ignore
                if bl.ContainsKey env.domain.DomainId then
                    bl[env.domain.DomainId] |> Set.iter (fun x -> 
                        match env.domain.Members |> Array.tryFind (fun y -> y.uid = x) with
                        | Some m -> sb.AppendLine m.name |> ignore
                        | None -> ())
                    sb.ToString() |> env.log
            | _ -> env.log "FriendStrategy not supported"
        }
    let ufrwlCommand() = {
        name = "ufrwl"
        description = "Update friend whitelist"
        accessibleDomain = CommandAccessibleDomain.Group
        privilege = CommandPrivilege.High
        execute = fun executer env args ->
            let rec go wl i =
                if i = args.Length then wl 
                elif args[i].StartsWith "-" then
                    let t = args[i][1..]
                    match 
                        env.domain.Members 
                        |> Array.tryFind (fun x -> x.name = t)
                    with
                    | Some mem ->
                        if Set.contains mem.uid wl then
                            env.log $"Remove member {t}({mem.uid}) from the whitelist";
                            go (Set.remove mem.uid wl) (i+1)
                        else
                            env.log $"Member {t} is not in the whitelist"; go wl (i+1)
                    | _ -> env.log $"Member {t} not found"; go wl (i+1)
                else
                    match 
                        env.domain.Members 
                        |> Array.tryFind (fun x -> x.name = args[i])
                    with
                    | Some mem ->
                        env.log $"Add member {args[i]}({mem.uid}) to the whitelist"
                        go (Set.add mem.uid wl) (i+1)
                    | _ -> env.log $"Member {args[i]} not found"; go wl (i+1)
            match env.bot.FriendStrategy with
            | StrategyFriendWhitelist wl ->
                if wl.ContainsKey env.domain.DomainId |> not then wl.Add(env.domain.DomainId, Set.empty)
                wl[env.domain.DomainId] <- go wl[env.domain.DomainId] 0
            | _ -> env.log "FriendStrategy not supported"
        }
    let ufrblCommand() = {
        name = "ufrbl"
        description = "Update friend blacklist"
        accessibleDomain = CommandAccessibleDomain.Group
        privilege = CommandPrivilege.High
        execute = fun executer env args ->
            let rec go wl i =
                if i = args.Length then wl 
                elif args[i].StartsWith "-"  then
                    let t = args[i][1..]
                    match 
                        env.domain.Members 
                        |> Array.tryFind (fun x -> x.name = t)
                    with
                    | Some mem ->
                        if Set.contains mem.uid wl then
                            env.log $"Remove member {t}({mem.uid}) from the blacklist";
                            go (Set.remove mem.uid wl) (i+1)
                        else
                            env.log $"Member {t} is not in the blacklist"; go wl (i+1)
                    | _ -> env.log $"Member {t} not found"; go wl (i+1)
                else
                    match 
                        env.domain.Members 
                        |> Array.tryFind (fun x -> x.name = args[i])
                    with
                    | Some mem ->
                        env.log $"Add member {args[i]}({mem.uid}) to the blacklist"
                        go (Set.add mem.uid wl) (i+1)
                    | _ -> env.log $"Member {args[i]} not found"; go wl (i+1)
            match env.bot.FriendStrategy with
            | StrategyFriendBlacklist bl ->
                if bl.ContainsKey env.domain.DomainId |> not then bl.Add(env.domain.DomainId, Set.empty)
                bl[env.domain.DomainId] <- go bl[env.domain.DomainId] 0
            | _ -> env.log "FriendStrategy not supported"
        }

    let commands() = [
            versionCommand()
            clearCommand()
            helpCommand()
            echoCommand()
            lsexecCommand()
            lsfrwlCommand()
            lsfrblCommand()
            ufrwlCommand()
            ufrblCommand()
        ]
module AestasBot =
    let inline tryGetModel (bot: AestasBot) = bot.Model
    let inline bindModel (bot: AestasBot) model =
        bot.BindModel model
    let inline bindDomain (bot: AestasBot) domain =
        bot.BindDomain domain
    let inline addExtraData (bot: AestasBot) (key: string) (value: obj) =
        bot.AddExtraData key value
    //let inline getPrimaryCommands() =
    //    (Builtin.operators.Values |> List.ofSeq) @ (Builtin.commands.Values |> List.ofSeq)
    let inline addCommandExecuter (bot: AestasBot) key executer =
        bot.AddCommandExecuter key executer
    let inline removeCommandExecuter (bot: AestasBot) key =
        bot.RemoveCommandExecuter key
    let inline addContentParser (bot: AestasBot) (ctor: ContentParser, name: string, tip: AestasBot -> StringBuilder -> unit) =
        bot.ContentParsers.Add(name, (ctor, tip))
    let inline updateExtraData (bot: AestasBot) (key: string) (value: obj) =
        bot.ExtraData[key] <- value
    // let inline updateCommand (bot: AestasBot) key executer =
    //     if bot.Commands.ContainsKey cmd.Name then bot.Commands.[cmd.Name] <- struct(typeof<'u>, typeof<'v>, cmd)
    //     else failwith $"Can only update existing item"
    let inline updateContentParser (bot: AestasBot) (ctor: ContentParser, name: string, tip: AestasBot -> StringBuilder -> unit) =
        if bot.ContentParsers.ContainsKey name then bot.ContentParsers[name] <- (ctor, tip)
        else failwith $"Can only update existing item"
    let inline addCommandExecuters (bot: AestasBot) (cmds: (string * CommandExecuter) list) =
        cmds |> List.iter (fun (k, v) -> bot.AddCommandExecuter k v)
    let inline addContentParsers (bot: AestasBot) contentParsers =
        contentParsers |> List.iter (addContentParser bot)
    let inline addSystemInstruction (bot: AestasBot) systemInstruction =
        bot.SystemInstruction <- systemInstruction
    let inline addSystemInstructionBuilder (bot: AestasBot) systemInstructionBuilder =
        match bot.SystemInstructionBuilder with
        | None -> bot.SystemInstructionBuilder <- systemInstructionBuilder |> PipeLineChain.singleton |> Some
        | Some x -> x.Bind systemInstructionBuilder
    let inline updateSystemInstruction (bot: AestasBot) systemInstruction =
        bot.SystemInstruction <- systemInstruction
    let inline updateSystemInstructionBuilder (bot: AestasBot) systemInstructionBuilder =
        bot.SystemInstructionBuilder <- Some systemInstructionBuilder
    let builtinCommandsExecuter() = Builtin.commands() |> Builtin.SpacedTextCommandExecuter
    let makeExecuterWithBuiltinCommands list = Builtin.commands() @ list |> Builtin.SpacedTextCommandExecuter
    let inline createBot (botParam: {|
        name: string
        model: ILanguageModelClient
        systemInstruction: string option
        systemInstructionBuilder: PipeLineChain<AestasBot*StringBuilder> option
        friendStrategy: BotFriendStrategy option
        contentLoadStrategy: BotContentLoadStrategy option
        contentParseStrategy: BotContentParseStrategy option
        messageReplyStrategy: BotMessageReplyStrategy option
        messageCacheStrategy: BotMessageCacheStrategy option
        contextStrategy: BotContextStrategy option
        inputPrefixBuilder: PrefixBuilder option
        userCommandPrivilege: (uint32*CommandPrivilege) list option
    |}) =
        let bot = AestasBot()
        let ``set?`` (target: 't -> unit) = function Some x -> target x | None -> ()
        bot.Name <- botParam.name
        bot.BindModel botParam.model
        ``set?`` bot.set_SystemInstruction botParam.systemInstruction
        bot.SystemInstructionBuilder <- botParam.systemInstructionBuilder
        ``set?`` bot.set_FriendStrategy botParam.friendStrategy
        ``set?`` bot.set_ContentLoadStrategy botParam.contentLoadStrategy
        ``set?`` bot.set_MessageReplyStrategy botParam.messageReplyStrategy
        ``set?`` bot.set_MessageCacheStrategy botParam.messageCacheStrategy
        ``set?`` bot.set_ContentParseStrategy botParam.contentParseStrategy
        ``set?`` bot.set_ContextStrategy botParam.contextStrategy
        bot.PrefixBuilder <- botParam.inputPrefixBuilder
        match botParam.userCommandPrivilege with
        | Some x -> x |> List.iter (fun (uid, privilege) -> bot.MemberCommandPrivilege.Add(uid, privilege))
        | _ -> ()
        bot
    let inline createBotShort name  =
        let bot = AestasBot()
        bot.Name <- name
        bot
module CommandExecuter =
    let inline tryAddCommand<'t> (executer: CommandExecuter) cmd =
        match executer with
        | :? CommandExecuter<'t> as e ->
            e.AddCommand cmd; Ok ()
        | _ -> Error "Type error"
    let inline getCommands (executer: CommandExecuter<'t>) = executer.Commands