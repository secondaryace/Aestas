namespace Aestas
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Linq
open System.Reflection
open System
open Prim

module rec Core =
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
    // type private _print = string -> unit
    // type private _chatHook = ILanguageModelClient -> unit
    // [<CustomEquality>]
    // [<NoComparison>]
    // type ID<'t when 't: equality> =
    //     struct
    //         val Value: 't
    //         new(value: 't) = {Value = value}
    //     end
    //     override this.Equals obj = 
    //         match obj with
    //         | :? ID<'t> as x -> this.Value = x.Value
    //         | _ -> false
    //     override this.GetHashCode() = this.Value.GetHashCode()
    //     static member op_Equality (a: ID<'t>, b: ID<'t>) = a.Value = b.Value
    type IMessageAdapter =
        abstract member MessageId: uint64 with get
        abstract member SenderId: uint32 with get
        abstract member Parse: unit -> AestasMessage
        abstract member Mention: targetMemberId: uint32 -> bool 
        abstract member Command: string option with get
        abstract member Collection: IMessageAdapterCollection with get
        abstract member Preview: string with get
        abstract member ParseAsPlainText: unit -> AestasMessage
    type IMessageAdapterCollection =
        inherit ICollection<IMessageAdapter>
        inherit IList<IMessageAdapter>
        inherit IReadOnlyList<IMessageAdapter>
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
        abstract member Messages: IMessageAdapterCollection with get
        abstract member Private: bool with get
        abstract member DomainId: uint32 with get
        abstract member Self: AestasChatMember with get
        abstract member Virtual: AestasChatMember with get
        abstract member Members: AestasChatMember[] with get
        abstract member Bot: AestasBot option with get, set
        abstract member Send: callback: (IMessageAdapter -> unit) -> contents: AestasContent list -> Async<Result<unit, string>>
        abstract member SendFile: data: byte[] -> Async<Result<unit, string>>
        abstract member Recall: messageId: uint64 -> Async<bool>
        abstract member Name: string with get
        abstract member OnReceiveMessage: IMessageAdapter -> Async<Result<unit, string>>
        default this.SendFile data =
            async {
                return Error "Not implemented"
            }
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
    type BotMessageReplyStrategy = 
        /// Won't reply to any message
        | StrategyReplyNone
        /// Only reply to messages that mentioned the bot or private messages
        | StrategyReplyOnlyMentionedOrPrivate
        /// Reply to all messages
        | StrategyReplyAll
        /// Reply to messages whose domain ID is in the whitelist
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
        | CurveInterestTruncateAfterTime of int<sec>
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
        | AestasImage of byte[]*string*int*int
        | AestasAudio of byte[]*string
        | AestasVideo of byte[]*string
        | AestasMention of AestasChatMember
        | AestasQuote of uint64
        | AestasFold of IMessageAdapterCollection
        | ProtocolSpecifyContent of IProtocolSpecifyContent
    [<Struct>]
    type AestasMessage = {
        sender: AestasChatMember
        content: AestasContent list
        mid: uint64
        }
    type InputConverterFunc = IMessageAdapterCollection -> AestasContent -> string
    /// domain * params * content
    type 't ContentCtor = AestasBot -> AestasChatDomain -> (string*string) list -> string -> 't
    type OverridePluginFunc = AestasContent ContentCtor
    type MappingContentCtor = Result<AestasContent, string> ContentCtor
    type ProtocolSpecifyContentCtor = Result<IProtocolSpecifyContent, string> ContentCtor
    type SystemInstructionBuilder = AestasBot -> StringBuilder -> unit
    type PrefixBuilder = AestasBot -> AestasMessage -> AestasMessage
    /// Bot class used in Aestas Core
    /// Create this directly
    type AestasBot() =
        let groups = AestasChatDomains()
        let extraData = Dictionary<string, obj>()
        let mutable originalSystemInstruction = ""
        /// Bot name, Default is "AestasBot"
        member val Name: string = "AestasBot" with get, set
        member val Model: ILanguageModelClient option = None with get, set
        member this.Domain
            with get domainId = groups[domainId]
            and set domainId (value: AestasChatDomain) =
                if value.DomainId = domainId then this.BindDomain value
                else failwith "Domain ID mismatch"
        member this.Domains = groups :> IReadOnlyDictionary<uint32, AestasChatDomain> 
        member val ContentLoadStrategy = StrategyLoadAll with get, set
        member val MessageReplyStrategy = StrategyReplyNone with get, set
        member val MessageCacheStrategy = StrategyCacheAll with get, set
        member val ContentParseStrategy = StrategyParseAndAlertError with get, set
        member val ContextStrategy = StrategyContextReserveAll with get, set
        member val Commands: Dictionary<string, ICommand> = Dictionary() with get
        member val CommandPrivilegeMap: Dictionary<uint32, CommandPrivilege> = Dictionary() with get
        member val MappingContentCtorTips: Dictionary<string, struct(MappingContentCtor*(AestasBot -> StringBuilder -> unit))> = Dictionary() with get
        member val ProtocolContentCtorTips: Dictionary<string, struct(ProtocolSpecifyContentCtor*(AestasBot->string))> = Dictionary() with get
        member val SystemInstructionBuilder: PipeLineChain<AestasBot*StringBuilder> option = None with get, set
        member val PrefixBuilder: PrefixBuilder option = None with get, set
        member val SubBots: Dictionary<obj, AestasBot> = Dictionary() with get
        member val SubBotDistributer: (IMessageAdapter option -> AestasChatDomain -> obj) option = None with get, set
        member _.TryGetExtraData
            with get key =
                if extraData.ContainsKey key then Some extraData[key] else None
        member _.ExtraData = extraData
        member this.AddExtraData (key: string) (value: obj) = 
            if extraData.ContainsKey key then failwith "Key already exists"
            else extraData.Add(key, value)
        member this.SystemInstruction 
            with get() = 
                match this.SystemInstructionBuilder with
                | Some b -> 
                    let sb = new StringBuilder(originalSystemInstruction)
                    (b.Invoke(this, sb) |> snd).ToString()
                | None -> originalSystemInstruction
            and set value = originalSystemInstruction <- value
        member this.BindDomain (domain: AestasChatDomain) = 
            if groups.ContainsKey domain.DomainId then
                groups[domain.DomainId].Bot <- None
                groups[domain.DomainId] <- domain
            else groups.Add(domain.DomainId, domain)
            domain.Bot <- Some this
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
        member this.Reply (domain: AestasChatDomain) (message: IMessageAdapter) =
            async {
                do! this.CheckContextLength domain
                match this.Model with
                | _ when groups.ContainsValue domain |> not -> return Error "This domain hasn't been added to this bot", ignore
                | _ when message.SenderId = domain.Self.uid -> return Ok [], ignore
                | _ when message.Command.IsSome ->
                    let command = message.Command.Value
                    Command.excecute {
                        bot = this
                        domain = domain
                        log = AestasText >> List.singleton >> domain.Send ignore >> Async.Ignore >> Async.Start
                        privilege = 
                            if this.CommandPrivilegeMap.ContainsKey message.SenderId then
                                this.CommandPrivilegeMap[message.SenderId]
                            else CommandPrivilege.Normal
                        } command
                    return Ok [], ignore
                | None -> return Error "No model binded to this bot", ignore
                | Some model ->
                    // check if illegal strategy
                    match this.MessageReplyStrategy, this.MessageCacheStrategy with
                    | StrategyReplyAll, StrategyCacheNone ->
                        failwith "Check your strategy, you can't reply to message without any cache"
                    | _ -> ()
                    let message' = 
                        match this.ContentLoadStrategy with
                        | StrategyLoadNone -> message.ParseAsPlainText()
                        | StrategyLoadAll ->  message.Parse()
                        | StrategyLoadByPredicate p when p message -> message.Parse()
                        | StrategyLoadOnlyMentionedOrPrivate when message.Mention domain.Self.uid || domain.Private -> message.Parse()
                        | _ -> message.ParseAsPlainText()
                    let message' =
                        match this.PrefixBuilder with
                        | Some b -> b this message'
                        | None -> message'
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
        temperature: float
        maxLength: int
        topK: int
        topP: float
        }
    type CacheMessageCallback = AestasMessage -> unit
    type ILanguageModelClient =
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
    type ICommand =
        abstract member Execute: CommandEnvironment -> Atom list -> Atom
        abstract member Name: string with get
        abstract member Help: string with get
        abstract member AccessibleDomain: CommandAccessibleDomain
        abstract member Privilege: CommandPrivilege
    type CommandEnvironment = {
        bot: AestasBot
        domain: AestasChatDomain
        log: string -> unit
        privilege: CommandPrivilege
        }
    type Ast =
        | Call of Ast list
        | Tuple of Ast list
        | Atom of Atom
    [<StructuredFormatDisplay("{Display}")>]
    type Atom =
        | AtomTuple of Atom list
        | AtomObject of Map<string, Atom>
        | Number of float
        | String of string
        | Identifier of string
        | Unit
        with
            member this.Display = 
                match this with
                | AtomTuple l -> $"""({l |> List.map (fun x -> x.Display) |> String.concat ", "})"""
                | AtomObject l -> $"""{{
{l 
    |> Map.map (fun k v -> $"  {k} = {v.Display}") |> Map.values |> String.concat "\n"}
}}"""
                | Number n -> n.ToString()
                | String s -> $"\"{s}\""
                | Identifier i -> i
                | Unit -> "()"
    type UnitMessageAdapter(message: AestasMessage, collection: UnitMessageAdapterCollection) =
        interface IMessageAdapter with
            member _.Mention uid = message.content |> List.exists (fun x -> 
                match x with
                | AestasMention m -> m.uid = uid
                | _ -> false)
            member _.Parse() = message
            member _.ParseAsPlainText() = {
                content = message.content 
                |> List.map (function | AestasText s -> s | _ -> "") 
                |> String.concat "" |> AestasText |> List.singleton
                mid = message.mid
                sender = message.sender
                }   
            member _.Collection = collection
            member _.Command =
                match message.content with
                | AestasText s::_ when s.StartsWith("#") -> 
                    Some s[1..]
                | _ -> None
            member _.MessageId = message.mid
            member _.Preview = message.content |> List.map (function | AestasText s -> s | _ -> "") |> String.concat ""
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
                UnitMessageAdapter({sender = bot; content = contents; mid = mid}, messages) |> callback
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
                let message = {sender = user; content = contents; mid = mid}
                mid <- mid + 1UL
                match! UnitMessageAdapter(message, messages) |> this.OnReceiveMessage with
                | Ok () -> return Ok (), mid - 1UL
                | Error e -> return Error e, mid - 1UL
            }
    type UnitClient() =
        interface ILanguageModelClient with
            member _.GetReply bot domain  = 
                async {
                    return Ok [], ignore
                }
            member _.CacheMessage bot domain message = ()
            member _.CacheContents bot domain contents = ()
            member _.ClearCache domain = ()
            member _.RemoveCache domain messageID = ()
        end
    module Console =
        let inline resolution() = struct(Console.WindowWidth, Console.WindowHeight)
        let inline clear() = Console.Clear()
        let inline cursorPos() = Console.GetCursorPosition()
        let inline setCursor x y = 
            let x, y = min (Console.WindowWidth-1) x, min (Console.WindowHeight-1) y
            let x, y = max 0 x, max 0 y
            Console.SetCursorPosition(x, y)
        let inline write (s: string) = Console.Write s
        let inline setColor (c: ConsoleColor) = Console.ForegroundColor <- c
        let inline resetColor() = Console.ResetColor()
        let inline writeColor (s: string) (c: ConsoleColor) = setColor c; write s; resetColor()
        let inline writeUnderlined (s: string) = write $"\x1B[4m{s}\x1B[0m"
        let inline writeColorUnderlined (s: string) (c: ConsoleColor) = setColor c; writeUnderlined s; resetColor()
        let inline setCursorVisible () = Console.CursorVisible <- true
        let inline setCursorInvisible () = Console.CursorVisible <- false
        let inline measureChar (c: char) =
            if c = '\n' then 0
            elif c = '\r' then 0
            // 0x4e00 - 0x9fbb is cjk
            elif int c >= 0x4e00 && int c <=0x9fbb then 2
            elif int c >= 0xff00 && int c <=0xffef then 2
            else 1   
        let inline measureString (s: ReadOnlyMemory<char>) =
            let rec go i l =
                if i >= s.Length then l else
                go (i+1) (l+measureChar s.Span[i])
            go 0 0
        let inline measureString' (s: string) = s.AsMemory() |> measureString
        let inline ( ..+ ) (struct(x, y)) (struct(x', y')) = struct(x+x', y+y')
        let inline ( ..- ) (struct(x, y)) (struct(x', y')) = struct(x-x', y-y')
        /// x * y * w * h -> x' * y'
        type DrawCursor<'t> = 't -> struct(int*int*int*int) -> struct(int*int)
        let stringDrawCursor (s: string) (struct(x, y, w, h)) =
            let s = s.AsMemory()
            let w = Console.WindowWidth |> min w
            let rec draw x y (s: ReadOnlyMemory<char>) =
                if y >= h || s.Length = 0 then struct(x, y) else
                Console.SetCursorPosition(x, y)
                let rec calcRest i l =
                    if i >= s.Length then i, i
                    elif l+x > w then i-2, i-2
                    elif l+x = w then i, i
                    elif i+1 < s.Length && s.Span[i] = '\n' && s.Span[i+1] = '\r' then i, i+2
                    elif s.Span[i] = '\n' then i, i+1
                    else calcRest (i+1) (l+measureChar s.Span[i])
                let delta, trim = calcRest 0 0
                s.Slice(0, delta) |> Console.Write
                draw x (y+1) (s.Slice trim)
            draw x y s
        let logEntryDrawCursor (this: Logger.LogEntry) st =
            Console.ForegroundColor <- Logger.levelToColor this.level
            let s = this.Print()
            let ret = stringDrawCursor s st in Console.ResetColor(); ret
        [<AbstractClass>]
        type CliView(_posX: int, _posY: int, _width: int, _height: int) =
            member val Children: CliView arrList option = None with get, set
            member val Parent: CliView option = None with get, set
            abstract member Draw: unit -> unit
            abstract member HandleInput: ConsoleKeyInfo -> unit
            abstract member Update: unit -> unit
            member this.Position = struct(_posX, _posY) ..+ match this.Parent with | Some p -> p.Position | _ -> struct(0, 0)
            member this.X = _posX + match this.Parent with | Some p -> p.X | _ -> 0
            member this.Y = _posY + match this.Parent with | Some p -> p.Y | _ -> 0
            member this.Width = _width
            member this.Height = _height
            member this.Size = struct(_width, _height)
            abstract member Append: CliView -> unit
            abstract member Remove: CliView -> unit
            default this.Draw() = 
                match this.Children with
                | None -> ()
                | Some c -> c |> ArrList.iter (fun x -> x.Draw())
            default this.HandleInput(info: ConsoleKeyInfo) = 
                match this.Children with
                | None -> ()
                | Some c -> c |> ArrList.iter (fun x -> x.HandleInput info)
            default this.Update() =
                match this.Children with
                | None -> ()
                | Some c -> c |> ArrList.iter (fun x -> x.Update())
            default this.Append(view: CliView) = 
                match this.Children with
                | None -> 
                    view.Parent <- Some this
                    this.Children <- view |> ArrList.singleton |> Some
                | Some c -> 
                    view.Parent <- Some this
                    c.Add view
            default this.Remove(view: CliView) =
                match this.Children with
                | None -> failwith "No children"
                | Some c -> 
                    view.Parent <- None
                    c.Remove view |> ignore
        type PanelView(posX: int, posY: int, width: int, height: int) =
            inherit CliView(posX, posY, width, height)
        type TextView(text: string, color: ConsoleColor option, posX: int, posY: int) =
            inherit CliView(posX, posY, measureString' text, 1)
            override this.Draw() =
                match color with
                | Some c -> 
                    Console.ForegroundColor <- c
                    setCursor this.X this.Y
                    Console.Write text
                    Console.ResetColor()
                | None -> 
                    setCursor this.X this.Y
                    Console.Write text
                base.Draw()
        type DynamicObjectView<'t>(object: unit -> 't, drawCursor: DrawCursor<'t>, posX: int, posY: int, width: int, height: int) =
            inherit CliView(posX, posY, width, height)
            override this.Draw() =
                drawCursor (object()) struct(this.X, this.Y, this.X+width, this.Y+height) |> ignore
        type InputView(action: string -> unit,posX: int, posY: int, width: int, height: int) =
            inherit CliView(posX, posY, width, height)
            let mutable text: string option = None
            override this.HandleInput(info: ConsoleKeyInfo) =
                match info.Key, info.KeyChar, text with
                | _, '：', None
                | _, ':', None  -> 
                    setColor ConsoleColor.Cyan
                    setCursorVisible()
                    setCursor this.X this.Y
                    let s = Console.ReadLine()
                    if s.Length > width then
                        text <- s.Substring(0, width) |> Some
                    else
                        text <- Some s
                    resetColor()
                    setCursorInvisible()
                | ConsoleKey.Backspace, _, Some _ ->
                    text <- None
                | ConsoleKey.Enter, _, Some s ->
                    action s
                    text <- None
                | _ -> ()
            override this.Draw() =
                setCursor this.X this.Y
                match text with
                | None -> new string(' ', width) |> writeUnderlined
                | Some s -> 
                    s |> writeUnderlined
                    let l = measureString' s
                    if l < width then new string(' ', width-l) |> writeUnderlined
        type ButtonView(func: bool -> bool, text: string, color: ConsoleColor option, activeColor: ConsoleColor option, key: ConsoleKey, posX: int, posY: int) =
            inherit CliView(posX, posY, measureString' text, 1)
            member val Active = false with get, set
            override this.HandleInput(info: ConsoleKeyInfo) =
                match info.Key with
                | x when x = key -> 
                    this.Active <- func this.Active
                | _ -> ()
            override this.Draw() =
                match color, activeColor, this.Active with
                | Some c, _, false
                | _, Some c, true ->
                    Console.ForegroundColor <- c
                    setCursor this.X this.Y
                    Console.Write text
                    Console.ResetColor()
                | _ -> 
                    setCursor this.X this.Y
                    Console.Write text
                base.Draw()
        type TabView(tabs: (string*CliView) arrList, asLeft: ConsoleKey, asRight: ConsoleKey, posX: int, posY: int, width: int, height: int) as this =
            inherit CliView(posX, posY, width, height)
            let update() =
                this.Children <- 
                    [let v' = PanelView(this.X, this.Y+2, width, height-2) in 
                        tabs |> ArrList.iter(fun (_, v) -> v'.Append v);
                        v'.Parent <- Some this;
                        v' :> CliView]
                    |> arrList
                    |> Some
            do update()
            member _.Tabs = tabs
            member val Index = 0 with get, set
            override this.Update() = update()
            override this.Draw() = 
                let rec draw i x = 
                    if i >= this.Tabs.Count || x > this.X+width then () else
                    if i <> 0 then Console.Write '|'
                    setCursor x this.Y
                    let name, _ = this.Tabs[i]
                    if i = this.Index then 
                        Console.BackgroundColor <- ConsoleColor.White; Console.ForegroundColor <- ConsoleColor.Black
                    name.AsMemory().Slice(0, min name.Length (this.X+width-x)) |> Console.Write
                    Console.BackgroundColor <- ConsoleColor.DarkGray; Console.ForegroundColor <- ConsoleColor.White
                    draw (i+1) (x+name.Length+1)
                Console.BackgroundColor <- ConsoleColor.DarkGray; Console.ForegroundColor <- ConsoleColor.White
                if ArrList.fold (fun acc (n: string, _) -> acc + n.Length+1) 0 this.Tabs[0..min (this.Tabs.Count-1) (this.Index+1)] >= width then
                    draw this.Index this.X
                else
                    draw 0 this.X
                Console.ResetColor()
                setCursor this.X (this.Y+1)
                let pages = $" {this.Index+1}/{this.Tabs.Count} "
                let rec draw x = if x >= this.X+width-pages.Length+1 then () else (Console.Write '─'; draw (x+1))
                draw this.X
                Console.Write pages
                this.Children.Value[0].Children.Value[this.Index].Draw()
            override this.HandleInput(info: ConsoleKeyInfo) =
                match info.Key with
                | x when x = asLeft -> this.Index <- (this.Index+this.Tabs.Count-1)%this.Tabs.Count
                | x when x = asRight -> this.Index <- (this.Index+1)%this.Tabs.Count
                | _ -> this.Children.Value[0].Children.Value[this.Index].HandleInput info
            override this.Append _ = failwith "TabView doesn't support Append"
            override this.Remove _ = failwith "TabView doesn't support Remove"
        type VerticalTabView(tabs: (string*CliView) arrList, asUp: ConsoleKey, asDown: ConsoleKey, posX: int, posY: int, width: int, height: int, leftPanelWidth: int) as this =
            inherit CliView(posX, posY, width, height)
            let update() =
                this.Children <- 
                    [let v' = PanelView(this.X+leftPanelWidth, this.Y, width-leftPanelWidth, height) in 
                        tabs |> ArrList.iter(fun (_, v) -> v'.Append v);
                        v'.Parent <- Some this;
                        v' :> CliView]
                    |> arrList
                    |> Some
            do update()
            member _.Tabs = tabs
            member val Index = 0 with get, set
            override this.Update() = update()
            override this.Draw() = 
                let rec draw i y = 
                    if i >= this.Tabs.Count || y > this.Y+height then () else
                    setCursor this.X y
                    let name = (fst this.Tabs[i]).AsMemory()
                    if i = this.Index then 
                        Console.BackgroundColor <- ConsoleColor.White; Console.ForegroundColor <- ConsoleColor.Black
                    let len = measureString name
                    if len >= leftPanelWidth then
                        name.Slice(0, leftPanelWidth-1) |> Console.Write
                    else
                        name |> Console.Write
                        new string(' ', leftPanelWidth-len) |> Console.Write
                    Console.BackgroundColor <- ConsoleColor.DarkGray; Console.ForegroundColor <- ConsoleColor.White
                    draw (i+1) (y+1)
                Console.BackgroundColor <- ConsoleColor.DarkGray; Console.ForegroundColor <- ConsoleColor.White
                if this.Index > height then
                    draw (this.Index-height) this.Y
                else
                    draw 0 this.Y
                Console.ResetColor()
                let rec draw y = if y >= this.Y+height then () else (setCursor (this.X+leftPanelWidth-1) y;Console.Write '|'; draw (y+1))
                draw this.Y
                this.Children.Value[0].Children.Value[this.Index].Draw()
            override this.HandleInput(info: ConsoleKeyInfo) =
                match info.Key with
                | x when x = asUp -> this.Index <- (this.Index+this.Tabs.Count-1)%this.Tabs.Count
                | x when x = asDown -> this.Index <- (this.Index+1)%this.Tabs.Count
                | _ -> this.Children.Value[0].Children.Value[this.Index].HandleInput info
            override this.Append _ = failwith "TabView doesn't support Append"
            override this.Remove _ = failwith "TabView doesn't support Remove"
        type VerticalListView<'t>(source: IReadOnlyList<'t>, drawCursor: DrawCursor<'t>, asUp: ConsoleKey, asDown: ConsoleKey, posX: int, posY: int, width: int, height: int) =
            inherit CliView(posX, posY, width, height)
            member val Source = source with get, set
            member val Index = 0 with get, set
            override this.Draw() =
                this.Index <- this.Index |> min (this.Source.Count-1) |> max 0
                let rec draw i y =
                    if i >= this.Source.Count || y >= this.Y+height then () else
                    let struct(_, y) = drawCursor this.Source[i] struct(this.X, y, this.X+width, this.Y+height)
                    draw (i+1) y
                draw this.Index this.Y
                base.Draw()
            override this.HandleInput(info: ConsoleKeyInfo) =
                match info.Key with
                | x when x = asUp -> this.Index <- max 0 (this.Index-1)
                | x when x = asDown -> this.Index <- min (this.Source.Count-1) (this.Index+1)
                | _ -> ()
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
                member this.Parse() = {sender = sender; content = [AestasText msg]; mid = (this :> IMessageAdapter).MessageId}
                member _.Collection = collection
                member this.Command = if msg.StartsWith '#' then msg.Substring 1 |> Some else None
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
                        do! consoleMessage |> consoleChat.OnReceiveMessage |> Async.Ignore
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
    module Command =
        module Lexer =
            type Token =
                | TokenSpace
                | TokenPrint
                | TokenFloat of float
                | TokenString of string
                /// a, bar, 变量 etc.
                | TokenIdentifier of string
                /// '<|'
                | TokenLeftPipe
                /// '|>'
                | TokenRightPipe
                /// '('
                | TokenLeftRound
                /// ')'
                | TokenRightRound
                /// '['
                | TokenLeftSquare
                /// ']'
                | TokenRightSquare
                /// '{'
                | TokenLeftCurly
                /// '}'
                | TokenRightCurly
                /// '<-'
                | TokenLeftArrow
                /// '->'
                | TokenRightArrow
                /// '|'
                | TokenPipe
                | TokenNewLine
                /// '.'
                | TokenDot
                /// ','
                | TokenComma
                /// ';'
                | TokenSemicolon
                /// ':'
                | TokenColon
                /// string: Error message
                | TokenError of string
            type private _stDict = IReadOnlyDictionary<string, Token>
            type Macros = IReadOnlyDictionary<string, string>
            type MaybeDict = 
                | AValue of Token
                | ADict of Dictionary<char, MaybeDict> 
            /// A pack of language primitive informations
            type LanguagePack = {keywords: _stDict; operatorChars: char array; operators: Dictionary<char, MaybeDict>; newLine: char array}
            /// Scan the whole source code
            let scan (lp: LanguagePack) (source: string) (macros: Macros) =
                let rec checkBound (k: string) (s: string) =
                    let refs = s.Split(' ') |> Array.filter (fun s -> s.StartsWith '$')
                    let mutable flag = false
                    for p in macros do
                        flag <- flag || (refs.Contains p.Key && p.Value.Contains k)
                    flag
                let mutable flag = false
                for p in macros do
                    flag <- flag || checkBound p.Key p.Value
                if flag then [TokenError("Macro looped reference detected")]
                else scan_ (arrList()) lp source macros |> List.ofSeq
            let scanWithoutMacro (lp: LanguagePack) (source: string)=
                scan_ (arrList()) lp source (readOnlyDict []) |> List.ofSeq
            let rec private scan_ (tokens: Token arrList) (lp: LanguagePack) (source: string) (macros: Macros) = 
                let cache = StringBuilder()
                let rec innerRec (tokens: Token arrList) cursor =
                    if cursor >= source.Length then tokens else
                    let lastToken = if tokens.Count = 0 then TokenNewLine else tokens[^0]
                    cache.Clear() |> ignore
                    match source[cursor] with
                    | ' ' -> 
                        if (lastToken = TokenNewLine || lastToken = TokenSpace) |> not then tokens.Add(TokenSpace)
                        innerRec tokens (cursor+1)
                    | '\"' ->
                        let (cursor, eof) = scanString lp (cursor+1) source cache
                        if eof then tokens.Add (TokenError "String literal arrives the end of file")
                        else tokens.Add (TokenString (cache.ToString()))
                        innerRec tokens cursor
                    | c when Array.contains c lp.newLine ->
                        if lastToken = TokenNewLine |> not then 
                            if lastToken = Token.TokenSpace then tokens[^0] <- TokenNewLine
                            else tokens.Add(TokenNewLine)
                        innerRec tokens (cursor+1)
                    | c when Array.contains c lp.operatorChars ->
                        if c = '(' && source.Length-cursor >= 2 && source[cursor+1] = '*' then
                            let (cursor, eof) = scanComment lp (cursor+2) source cache
                            if eof then tokens.Add (TokenError "Comment arrives the end of file")
                            innerRec tokens cursor
                        else
                            let cursor = scanSymbol lp cursor source cache
                            let rec splitOp (d: Dictionary<char, MaybeDict>) i =
                                if i = cache.Length |> not && d.ContainsKey(cache[i]) then
                                    match d[cache[i]] with
                                    | ADict d' -> splitOp d' (i+1)
                                    | AValue v -> 
                                        tokens.Add v
                                        splitOp lp.operators (i+1)
                                else if d.ContainsKey('\000') then
                                    match d['\000'] with AValue t -> tokens.Add t | _ -> failwith "Impossible"
                                    splitOp lp.operators i
                                else if i = cache.Length then ()
                                else tokens.Add (TokenError $"Unknown symbol {cache[if i = cache.Length then i-1 else i]}")
                            splitOp lp.operators 0
                            innerRec tokens cursor
                    | c when isNumber c ->
                        let (cursor, isFloat) = scanNumber lp cursor source cache false
                        let s = cache.ToString()
                        tokens.Add(TokenFloat (Double.Parse s))
                        innerRec tokens cursor
                    | '$' ->
                        let cursor = scanIdentifier lp (cursor+1) source cache
                        let s = cache.ToString()
                        if s = "" || macros.ContainsKey s |> not then tokens.Add (TokenError $"Macro {s} is not defined")
                        else 
                            scan_  tokens lp macros[s] macros |> ignore
                        innerRec tokens cursor
                    | _ -> 
                        let cursor = scanIdentifier lp cursor source cache
                        let s = cache.ToString()
                        match s with
                        | s when lp.keywords.ContainsKey s -> tokens.Add(lp.keywords[s])
                        | _ -> tokens.Add(TokenIdentifier s)
                        innerRec tokens cursor
                innerRec tokens 0
            let rec private scanIdentifier lp cursor source cache =
                let current = source[cursor]
                if current = ' ' || current = '\"' || Array.contains current lp.newLine || Array.contains current lp.operatorChars then
                    cursor
                else 
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then cursor+1 else scanIdentifier lp (cursor+1) source cache
            let rec private scanNumber lp cursor source cache isFloat =
                let current = source[cursor]
                if isNumber current then
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then (cursor+1, isFloat) else scanNumber lp (cursor+1) source cache isFloat
                else if current = '.'  then 
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then (cursor+1, true) else scanNumber lp (cursor+1) source cache true
                else if current = 'e' && source.Length - cursor >= 2 && (isNumber source[cursor+1] || source[cursor+1] = '-') then 
                    cache.Append current |> ignore
                    scanNumber lp (cursor+1) source cache isFloat
                else (cursor, isFloat)
            let rec private scanSymbol lp cursor source cache =
                let current = source[cursor]
                if Array.contains current lp.operatorChars then
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then cursor+1 else scanSymbol lp (cursor+1) source cache
                else cursor
            let rec private scanString lp cursor source cache =
                let current = source[cursor]
                if current = '\"' then (cursor+1, false)
                else if source.Length-cursor >= 2 && current = '\\' then
                    let next = source[cursor+1]
                    (match next with
                    | 'n' -> cache.Append '\n'
                    | 'r' -> cache.Append '\r'
                    | '\\' -> cache.Append '\\'
                    | '\"' -> cache.Append '\"'
                    | _ -> cache.Append '?') |> ignore
                    if cursor+1 = source.Length-1 then (cursor+1, true) else scanString lp (cursor+2) source cache
                else
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then (cursor, true) else scanString lp (cursor+1) source cache
            let rec private scanComment lp cursor source cache =
                if source[cursor] = '*' && source[cursor+1] = ')' then (cursor+2, false)
                else if cursor < source.Length-1 then scanComment lp (cursor+1) source cache
                else (cursor, true)
            let private isNumber c = c <= '9' && c >= '0'
            let makeLanguagePack (keywords: _stDict) (operators: _stDict) (newLine: char array) =
                let rec makeOpTree (operators: _stDict) =
                    let dictdict = Dictionary<char, MaybeDict>()
                    let rec addToDict (m: ReadOnlyMemory<char>) (t: Token) (d: Dictionary<char, MaybeDict>) =
                        let s = m.Span
                        if s.Length = 1 then
                            if d.ContainsKey(s[0]) then
                                match d[s[0]] with ADict d -> d.Add('\000', AValue t) | _ -> failwith "Impossible"
                            else d.Add(s[0], AValue t)
                        else
                            if d.ContainsKey(s[0]) then
                                match d[s[0]] with
                                | AValue v ->
                                    let d' = Dictionary<char, MaybeDict>()
                                    d'.Add('\000', AValue v)
                                    d[s[0]] <- ADict (Dictionary<char, MaybeDict>(d'))
                                | _ -> ()
                            else d.Add(s[0], ADict (Dictionary<char, MaybeDict>()))
                            match d[s[0]] with ADict d -> addToDict (m.Slice 1) t d | _ -> failwith "Impossible"
                    let rec makeDict (dictdict: Dictionary<char, MaybeDict>) (p: KeyValuePair<string, Token>) =
                        addToDict (p.Key.AsMemory()) p.Value dictdict
                    Seq.iter (makeDict dictdict) operators
                    dictdict
                let opChars = ResizeArray<char>()
                Seq.iter 
                <| (fun s -> Seq.iter (fun c -> if opChars.Contains c |> not then opChars.Add c) s)
                <| operators.Keys
                ()
                {keywords = keywords; operatorChars = opChars.ToArray(); operators = makeOpTree operators; newLine = newLine}
        module Parser =
            open Lexer
            let inline private eatSpace tokens =
                match tokens with
                | TokenSpace::r -> r
                | _ -> tokens
            let inline private eatSpaceAndNewLine tokens =
                match tokens with
                | TokenSpace::r -> r
                | TokenNewLine::r -> r
                | _ -> tokens
            let inline private eatSpaceOfTuple (t, tokens, errors) =
                match tokens with
                | TokenSpace::r -> t, r, errors
                | _ -> t, tokens, errors
            let parseAbstractTuple seperator multiLine makeTuple spSingleItem parseItem failMsg failValue tokens errors = 
                let rec innerRec tokens errors result =
                    match eatSpace tokens with
                    | TokenNewLine::r when multiLine ->
                        match r with
                        | TokenRightCurly::_ | TokenRightSquare::_ | TokenRightRound::_ -> 
                            result |> List.rev, tokens, errors
                        | _ ->
                            let item, tokens, errors = parseItem (eatSpaceAndNewLine r) errors
                            innerRec tokens errors (item::result)
                    | x::r when x = seperator ->
                        let item, tokens, errors = parseItem r errors
                        innerRec tokens errors (item::result)
                    | _ -> result |> List.rev, tokens, errors
                match parseItem ((if multiLine then eatSpaceAndNewLine else eatSpace) tokens) errors with
                | item, tokens, [] ->
                    let items, tokens, errors = innerRec tokens errors [item]
                    match errors with 
                    | [] -> (match items with | [e] when spSingleItem -> e | _ -> makeTuple items), tokens, errors
                    | _ -> failValue, tokens, $"{failMsg} item, but found {tokens[0..2]}"::errors
                | _, _, errors -> failValue, tokens, $"{failMsg} tuple, but found {tokens[0..2]}"::errors
            /// tuple = tupleItem {"," tupleItem}
            let parse tokens errors = 
                parseAbstractTuple TokenComma false Tuple true parseTupleItem "Expected expression" (Atom Unit) tokens errors
            let rec parseTupleItem (tokens: Token list) (errors: string list) =
                let l, tokens, errors = parseExpr tokens errors
                match eatSpace tokens with
                | TokenPipe::r
                | TokenRightPipe::r ->
                    let token, tokens, errors = parseTupleItem r errors
                    match token with
                    | Call args ->
                        Call (args@[l]), tokens, errors
                    | _ -> failwith "Impossible"
                | _ -> l, tokens, errors
            let rec parseExpr (tokens: Token list) (errors: string list) =
                let rec go tokens acc errors =
                    match tokens with
                    | TokenSpace::TokenPipe::_
                    | TokenSpace::TokenRightPipe::_
                    | TokenSpace::TokenRightRound::_ -> acc |> List.rev, tokens, errors
                    | TokenSpace::TokenLeftRound::TokenRightRound::xs 
                    | TokenSpace::TokenLeftRound::TokenSpace::TokenRightRound::xs ->
                        go xs ((Atom Unit)::acc) errors
                    | TokenSpace::TokenLeftRound::xs
                    | TokenLeftRound::xs ->
                        match parse xs errors |> eatSpaceOfTuple with
                        | ast, TokenRightRound::rest, errors -> go rest (ast::acc) errors
                        | _, x::_, _ -> [], tokens, $"Expected \")\", but found \"{x}\""::errors
                        | _ -> [], tokens, "Unexpected end of input"::errors
                    | TokenSpace::xs ->
                        let ast, rest, errors = parseAtom xs errors
                        go rest ((Atom ast)::acc) errors
                    | _ -> acc |> List.rev, tokens, errors
                match tokens |> eatSpace with
                | TokenLeftRound::xs ->
                    match parse xs errors |> eatSpaceOfTuple with
                    | ast, TokenRightRound::rest, errors -> 
                        let func = ast
                        let args, tokens, errors = go rest [] errors
                        Call (func::args), tokens, errors
                    | _, x::_, _ -> Atom Unit, tokens, $"Expected \")\", but found \"{x}\""::errors
                    | _ -> Atom Unit, tokens, "Unexpected end of input"::errors
                | _ ->
                    let func, rest, errors = parseAtom tokens errors
                    let args, tokens, errors = go rest [] errors
                    Call (Atom func::args), tokens, errors
                //go tokens [] errors
            let rec parseAtom tokens errors =
                match tokens |> eatSpace with
                | TokenFloat x::xs -> Number x, xs, errors
                | TokenString x::xs -> String x, xs, errors
                | TokenIdentifier x::xs -> Identifier x, xs, errors
                | x -> Unit, x, $"Unexpected token \"{x}\""::errors
                    // let getCommands filter =
                    //     let ret = Dictionary<string, ICommand>()
                    //     (fun (t: Type) ->
                    //         t.GetCustomAttributes(typeof<AestasCommandAttribute>, false)
                    //         |> Array.iter (fun attr ->
                    //             if attr :?> AestasCommandAttribute |> filter then
                    //                 let command = Activator.CreateInstance(t) :?> ICommand
                    //                 let name = (attr :?> AestasCommandAttribute).Name
                    //                 ret.Add(name, command)
                    //         )
                    //     )|> Array.iter <| Assembly.GetExecutingAssembly().GetTypes()
                    //     ret
        let keywords = readOnlyDict [
            "print", Lexer.TokenPrint
        ]
        let symbols = readOnlyDict [
            "<|", Lexer.TokenLeftPipe
            "|>", Lexer.TokenRightPipe
            "<-", Lexer.TokenLeftArrow
            "->", Lexer.TokenRightArrow
            ":", Lexer.TokenColon
            ";", Lexer.TokenSemicolon
            "(", Lexer.TokenLeftRound
            ")", Lexer.TokenRightRound
            "[", Lexer.TokenLeftSquare
            "]", Lexer.TokenRightSquare
            "{", Lexer.TokenLeftCurly
            "}", Lexer.TokenRightCurly
            ".", Lexer.TokenDot
            ",", Lexer.TokenComma
            "|", Lexer.TokenPipe
        ]
        let newLine = [|'\n';'\r';'`'|]
        let rec private excecuteAst (env: CommandEnvironment) (ast: Ast) =
            match ast with
            | Tuple items ->
                let rec go acc = function
                | Call h::t ->
                    go (excecuteAst env (Call h)::acc) t
                | Tuple h::t ->
                    go (excecuteAst env (Tuple h)::acc) t
                | Atom h::t ->
                    go (h::acc) t
                | [] -> acc |> List.rev |> AtomTuple
                go [] items
            | Call args ->
                let func = args.Head
                match excecuteAst env func with
                | Identifier "if" ->
                    match excecuteAst env args.Tail.Head, args.Tail.Tail with
                    | Number flag, t::f::[] ->
                        if flag = 0. then
                            excecuteAst env f
                        else
                            excecuteAst env t
                    | _, _ -> env.log "if condition trueBranch falseBranch"; Unit
                | Identifier name when env.bot.Commands.ContainsKey name ->
                    match env.privilege with
                    | x when x < env.bot.Commands[name].Privilege -> 
                        env.log $"Permission denied"; Unit
                    | _ ->
                        let args = List.map (fun x -> excecuteAst env x) args.Tail
                        env.bot.Commands[name].Execute env args
                | Identifier name ->
                    env.log $"Command not found: {name}"
                    Unit
                | x -> 
                    env.log $"Expected identifier, but found {x}"
                    Unit
            | Atom x -> x
        let LanguagePack = Lexer.makeLanguagePack keywords symbols newLine
        let excecute (env: CommandEnvironment) (cmd: string) =
            try
            let tokens = Lexer.scanWithoutMacro LanguagePack cmd
            let ast, _, errors = Parser.parse tokens []
            Logger.logInfo[0] <| sprintf "%A, %A, %A" tokens ast errors
            match errors with
            | [] -> 
                match excecuteAst env ast with
                | Unit -> ()
                | x -> env.log <| x.ToString()
            | _ -> env.log <| String.Join("\n", "Error occured:"::errors)
            with ex -> env.log <| ex.ToString()
    module CommandHelper =
        /// Use this type to tell the parser how to parse the arguments
        type CommandParameters =
            /// To require a unit value parameter, Ctor: name
            | ParamUnit of Name: string
            /// To require a string value parameter, Ctor: name * default value
            | ParamString of Name: string*(string option)
            /// To require a identifier value parameter, Ctor: name * default value
            | ParamIdentifier of Name: string*(string option)
            /// To require a number value parameter, Ctor: name * default value
            | ParamNumber of Name: string*(float option)
            /// To require a tuple value parameter, Ctor: name * default value
            | ParamTuple of Name: string*(Atom list option)
            /// To require a object value parameter, Ctor: name * default value
            | ParamObject of Name: string*(Map<string, Atom> option)
        /// Parse result
        type CommandArguments =
            /// Indicates that the argument is not provided
            | ArgNone
            /// Indicates that the argument is provided a unit value
            | ArgUnit
            /// Indicates that the argument is provided a string value
            | ArgString of string
            /// Indicates that the argument is provided a identifier value
            | ArgIdentifier of Name: string
            /// Indicates that the argument is provided a number value
            | ArgNumber of Name: float
            /// Indicates that the argument is provided a tuple value
            | ArgTuple of Name: Atom list
            /// Indicates that the argument is provided a object value
            | ArgObject of Name: Map<string, Atom>
        let inline getParamName p =
            match p with 
            | ParamUnit n -> n
            | ParamString (n, _) -> n
            | ParamIdentifier (n, _) -> n
            | ParamNumber (n, _) -> n
            | ParamTuple (n, _) -> n
            | ParamObject (n, _) -> n
        let inline getParamDefaultValue p =
            match p with 
            | ParamUnit _ -> ArgNone
            | ParamString (_, d) -> match d with | Some x -> ArgString x | None -> ArgNone
            | ParamIdentifier (_, d) -> match d with | Some x -> ArgIdentifier x | None -> ArgNone
            | ParamNumber (_, d) -> match d with | Some x -> ArgNumber x | None -> ArgNone
            | ParamTuple (_, d) -> match d with | Some x -> ArgTuple x | None -> ArgNone
            | ParamObject (_, d) -> match d with | Some x -> ArgObject x | None -> ArgNone
        let parseArguments (params': CommandParameters seq) args =
            let params' = params' |> Seq.map (fun p -> getParamName p, p) |> Map.ofSeq
            let rec go params' args acc errors =
                match args with
                | [] -> params', acc, errors
                | Identifier x::v::t when params' |> Map.containsKey x ->
                    match v, params'[x] with
                    | String s, ParamString (_, _) -> go (Map.remove x params') t (Map.add x (ArgString s) acc) errors
                    | Identifier i, ParamIdentifier (_, _) -> go (Map.remove x params') t (Map.add x (ArgIdentifier i) acc) errors
                    | Number n, ParamNumber (_, _) -> go (Map.remove x params') t (Map.add x (ArgNumber n) acc) errors
                    | AtomTuple t', ParamTuple (_, _) -> go (Map.remove x params') t (Map.add x (ArgTuple t') acc) errors
                    | AtomObject o, ParamObject (_, _) -> go (Map.remove x params') t (Map.add x (ArgObject o) acc) errors
                    | Unit, ParamUnit _ -> go (Map.remove x params') t (Map.add x ArgUnit acc) errors
                    | _, ParamUnit _ -> go (Map.remove x params') t (Map.add x ArgUnit acc) errors
                    | _, _ -> go (Map.remove x params') t (Map.add x ArgUnit acc) ($"Couldn't parse argument {x}: Type mismatch"::errors)
                | Identifier x::[] when params' |> Map.containsKey x ->
                    match params'[x] with
                    | ParamUnit _ -> go (Map.remove x params') [] (Map.add x ArgUnit acc) errors
                    | _ -> go (Map.remove x params') [] (Map.add x ArgUnit acc) ($"Couldn't parse argument {x}: Unexpected end of input"::errors)
                | _ -> 
                    params', acc, $"""Couldn't parse argument {Map.keys params' |> Seq.map (fun x -> x.ToString()) |> String.concat ", "}: Bad input, ignored {args}"""::errors
            let params', args, errors = go params' args Map.empty []
            if params'.Count = 0 then args, errors else
            Map.fold (fun acc k v -> Map.add k (getParamDefaultValue v) acc) args params', errors
    module Builtin =
        // f >> g, AutoInit.inputConverters >> (Builtin.inputConverters messages)
        let rec modelInputConverters (domain: AestasChatDomain) = function
        | AestasText x -> x
        | AestasImage _ -> "#[image: not supported]"
        | AestasAudio _ -> "#[audio: not supported]"
        | AestasVideo _ -> "#[video: not supported]"
        | AestasMention x -> $"#[mention: {x.name}]"
        | AestasQuote x -> $"#[quote: {x}]"
        | AestasFold x -> $"#[foldedMessage: {x.Count} messages]"
        | ProtocolSpecifyContent x -> x.ToPlainText()
        | _ -> failwith "Could not handle this content type"
        let overridePrimCtor = dict' [
            "text", fun (domain: AestasChatDomain, param: (string*string) list, content) -> AestasText content
            "image", fun (domain, param, content) -> AestasImage (Array.zeroCreate<byte> 0, "image/png", 0, 0)
            "audio", fun (domain, param, content) -> AestasAudio (Array.zeroCreate<byte> 0, "audio/wav")
            "video", fun (domain, param, content) -> AestasVideo (Array.zeroCreate<byte> 0, "video/mp4")
            "mention", fun (domain, param, content) ->
                let content = content.Trim()
                match domain.Members |> Array.tryFind (fun x -> x.name = content) with
                | Some m -> AestasMention {uid = m.uid; name = m.name}
                | None -> AestasText $"[mention: {content}]"
        ]
        let overridePrimTip = [
            "mention", "Using format #[mention: name] to mention or at someone. e.g. #[mention: Stella]"
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
            let rec scanBracket i v f p =
                match botOut[i] with
                | '@' ->
                    let f = if cache.Length <> 0 then cache.ToString() else f
                    cache.Clear() |> ignore
                    let i, pr, _ = scanParam (i+1) ("", "")
                    scanBracket i v f (pr::p)
                | ':' -> 
                    let f = if cache.Length <> 0 then cache.ToString() else f
                    cache.Clear() |> ignore
                    scanBracket (i+1) v f p
                | ']' when v = 1 -> i+1, f, p, cache.ToString(), cache.Clear() |> ignore
                // ignore nested square brackets
                | '[' -> scanBracket (i+1) (v+1) f p
                | ']' -> scanBracket (i+1) (v-1) f p
                | _ ->
                    cache.Append botOut[i] |> ignore
                    if i+1 = botOut.Length then i+1, f, p, cache.ToString(), cache.Clear() |> ignore 
                    else scanBracket (i+1) v f p
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
                    let i, funcName, param, content, _ = scanBracket (i+2) 1 "" []
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
                    else if bot.MappingContentCtorTips.ContainsKey funcName then
                        match fst' bot.MappingContentCtorTips[funcName] bot domain param content with
                        | Ok content ->
                            content |> result.Add
                        | Error emsg ->
                            error $"<Error occured when parsing [{funcName}]: {emsg}>"
                    else if bot.ProtocolContentCtorTips.ContainsKey funcName then
                        match fst' bot.ProtocolContentCtorTips[funcName] bot domain param content with
                        | Ok content ->
                            content |> ProtocolSpecifyContent |> result.Add
                        | Error emsg ->
                            error $"<Error occured when parsing [{funcName}]: {emsg}>"
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
            bot.MappingContentCtorTips |> Dict.iter (fun name struct(ctor, tipBuilder) -> 
                sb.Append("**").Append(name).AppendLine("**:") |> ignore
                tipBuilder bot sb
                sb.AppendLine("") |> ignore)
            bot.ProtocolContentCtorTips |> Dict.iter (fun name struct(ctor, tip) -> sb.Append $"**{name}**:\n{tip bot}\n" |> ignore)
            bot, sb
        let buildPrefix (bot: AestasBot) (msg: AestasMessage) =
            {
                content = AestasText $"[{msg.sender.name}|{DateTime.Now:``yyyy-MM-dd HH:mm``}] "::msg.content
                sender = msg.sender
                mid = msg.mid
            }
        type IdentityCommand() =
            interface ICommand with
                member _.Name = "id"
                member _.Help = "Operator: x ... -> x"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    match args with
                    | [] -> Unit
                    | [x] -> x
                    | _ -> env.log "Expected one argument"; Unit
        type MakeTupleCommand() =
            interface ICommand with
                member _.Name = "tuple"
                member _.Help = "Operator: x y ... -> (x, y, ...)"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    args |> AtomTuple
        type EqualCommand() = //not yet implemented
            interface ICommand with
                member _.Name = "eq"
                member _.Help = "Operator: eq x y -> 1 if x = y, 0 otherwise"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    match args with
                    | x::y::[] -> 
                        match x, y with
                        | Number x, Number y -> if x = y then Number 1. else Number 0.
                        | String x, String y -> if x = y then Number 1. else Number 0.
                        | Identifier x, Identifier y -> if x = y then Number 1. else Number 0.
                        | _, _ -> env.log "Expected same type"; Number 0.
                    | _ -> env.log "Expected two arguments"; Number 0.
        type ProjectionCommand() =
            interface ICommand with
                member _.Name = "proj"
                member _.Help = "Operator: proj \"a\" ({a: x; b: y, ...}, {a: z; b: w, ...}, ...) -> (x, z, ...)"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    match args with
                    | Identifier x::AtomTuple ls::[] -> 
                        let rec go acc = function
                        | [] -> acc |> List.rev
                        | AtomObject o::t -> 
                            match o |> Map.tryFind x with
                            | Some v -> go (AtomObject(Map [x, v])::acc) t
                            | None -> env.log $"Key {x} not found"; go (Unit::acc) t
                        | _ -> env.log "Expected object"; go (Unit::acc) []
                        go [] ls |> AtomTuple
                    | AtomTuple xs::AtomTuple ls::[] ->
                        let rec go acc = function
                        | [] -> acc |> List.rev
                        | AtomObject o::t -> 
                            ((xs 
                            |> List.map (function
                                | Identifier x -> 
                                    match o |> Map.tryFind x with
                                    | Some v -> x, v
                                    | None -> env.log $"Key {x} not found"; "_", Unit
                                | _ -> env.log "Expected identifier"; "_", Unit)
                            |> Map |> AtomObject)::acc |> go) t
                        | _ -> env.log "Expected object"; go (Unit::acc) []
                        go [] ls |> AtomTuple
                    | _ -> env.log "Expected identifier and object tuple"; Unit
        type DummyIfCommand() =
            interface ICommand with
                member _.Name = "if"
                member _.Help = "Operator: if 0 x y -> y, if 1 x y -> x"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    args |> AtomTuple
        type VersionCommand() =
            interface ICommand with
                member _.Name = "version"
                member _.Help = "Print the version of Aestas"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    env.log $"Aestas version {version}"; Unit
        type DomainInfoCommand() =
            interface ICommand with
                member _.Name = "domaininfo"
                member _.Help = "Print the current domain info"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    Map [
                        "id", env.domain.DomainId |> float |> Number
                        "name", env.domain.Name |> String
                        "private", if env.domain.Private then Number 1. else Number 0.
                    ] |> AtomObject
        type ListDomainCommand() =
            interface ICommand with
                member _.Name = "lsdomain"
                member _.Help = "List all domains"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    let sb = StringBuilder()
                    env.bot.Domains |>
                    Seq.map (fun p ->
                        Map [
                            "id", p.Value.DomainId |> float |> Number
                            "name", p.Value.Name |> String
                            "private", if p.Value.Private then Number 1. else Number 0.
                        ] |> AtomObject)
                    |> List.ofSeq
                    |> AtomTuple
        type HelpCommand() =
            interface ICommand with
                member _.Name = "help"
                member _.Help = "List all commands"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    let sb = StringBuilder()
                    sb.Append "## Commands" |> ignore
                    env.bot.Commands |>
                    Dict.iter (fun _ v -> sb.Append $"\n* {v.Name}:\n   {v.Help}" |> ignore)
                    sb.ToString() |> env.log; Unit
        type DumpCommand() =
            interface ICommand with
                member _.Name = "dump"
                member _.Help = """Dump the cached context, the input index is from the end of the context | Usage: dump [from=count-1] [to=0]"""
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    let msgs = env.domain.Messages
                    let args, _ = args |> CommandHelper.parseArguments [
                        CommandHelper.ParamNumber("from", msgs.Count-1 |> float |> Some)
                        CommandHelper.ParamNumber("to", Some 0.)
                    ]
                    let s, t =
                        match args["from"], args["to"] with
                        | CommandHelper.ArgNumber x, CommandHelper.ArgNumber y -> msgs.Count-1-int x, msgs.Count-1-int y
                        | _ -> failwith "Should not happen"
                    let sb = StringBuilder()
                    sb.Append "## Cached Context:" |> ignore
                    let rec go i =
                        if i > t then ()
                        else
                            let m = msgs[i].Parse()
                            sb.Append $"\n* {m.sender.name}:\n   {m.content}\n  ({m.mid})" |> ignore
                            go (i+1)
                    go s
                    sb.ToString() |> env.log; Unit
        type ClearCommand() =
            interface ICommand with
                member _.Name = "clear"
                member _.Help = "Clear the cached context"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal                
                member _.Execute env args =
                    env.bot.ClearCachedContext env.domain
                    env.log "Cached context cleared"; Unit
        let operators = Map.ofList [
            "id", IdentityCommand() :> ICommand
            "tuple", MakeTupleCommand()
            "proj", ProjectionCommand()
            "if", DummyIfCommand()
        ]
        let commands =  Map.ofList [
            "version", VersionCommand() :> ICommand
            "domaininfo", DomainInfoCommand()
            "lsdomain", ListDomainCommand()
            "help", HelpCommand()
            "dump", DumpCommand()
            "clear", ClearCommand()
        ]
        // type BillionDollarBotClient() =
        //     interface ILanguageModelClient with
        //         member this.SendMessage bot domain message = (this :> ILanguageModelClient).SendContents bot domain message.content
        //         member _.SendContents bot domain contents =
        //             async {
        //                 let strs =  contents |> List.map (modelInputConverters domain.Messages)
        //                 strs |> String.concat " " |> modelOutputParser bot domain |> printfn "%A"
        //                 let response =
        //                     (strs |> String.concat " ")
        //                         .Replace("You ", "~!@#")
        //                         .Replace("I ", "You ")
        //                         .Replace("~!@#", "I ")
        //                         .Replace("you", "I ")
        //                         .Replace("?", "!")
        //                 return Ok [AestasText response], ignore
        //             }
        //         member _.CacheMessage bot domain message = ()
        //         member _.CacheContents bot domain contents = ()
        //         member _.ClearCache() = ()
    module BotHelper =
        let inline bindDomain (bot: AestasBot) domain =
            bot.BindDomain domain
        let inline addExtraData (bot: AestasBot) (key: string) (value: obj) =
            bot.AddExtraData key value
        let inline getPrimaryCommands() =
            (Builtin.operators.Values |> List.ofSeq) @ (Builtin.commands.Values |> List.ofSeq)
        let inline addCommand (bot: AestasBot) (cmd: ICommand) =
            bot.Commands.Add(cmd.Name, cmd)
        let inline addContentParser (bot: AestasBot) (ctor: MappingContentCtor, name: string, tip: AestasBot -> StringBuilder -> unit) =
            bot.MappingContentCtorTips.Add(name, (ctor, tip))
        let inline addProtocolContentCtorTip (bot: AestasBot) (ctor: ProtocolSpecifyContentCtor, name: string, tip: AestasBot -> string) =
            bot.ProtocolContentCtorTips.Add(name, (ctor, tip))
        let inline updateExtraData (bot: AestasBot) (key: string) (value: obj) =
            bot.ExtraData[key] <- value
        let inline updateCommand (bot: AestasBot) (cmd: ICommand) =
            if bot.Commands.ContainsKey cmd.Name then bot.Commands.[cmd.Name] <- cmd
            else failwith $"Can only update existing item"
        let inline updateContentParser (bot: AestasBot) (ctor: MappingContentCtor, name: string, tip: AestasBot -> StringBuilder -> unit) =
            if bot.MappingContentCtorTips.ContainsKey name then bot.MappingContentCtorTips.[name] <- (ctor, tip)
            else failwith $"Can only update existing item"
        let inline addCommands (bot: AestasBot) (cmds: ICommand list) =
            cmds |> List.iter (addCommand bot)
        let inline addContentParsers (bot: AestasBot) contentParsers =
            contentParsers |> List.iter (addContentParser bot)
        let inline addProtocolContentCtorTips (bot: AestasBot) ctorTips =
            ctorTips |> List.iter (addProtocolContentCtorTip bot)
        let inline addSystemInstruction (bot: AestasBot) systemInstruction =
            bot.SystemInstruction <- systemInstruction
        let inline addSystemInstructionBuilder (bot: AestasBot) systemInstructionBuilder =
            bot.SystemInstructionBuilder <- Some systemInstructionBuilder
        let inline updateSystemInstruction (bot: AestasBot) systemInstruction =
            bot.SystemInstruction <- systemInstruction
        let inline updateSystemInstructionBuilder (bot: AestasBot) systemInstructionBuilder =
            bot.SystemInstructionBuilder <- Some systemInstructionBuilder
        type BotParameter = {
            name: string
            model: ILanguageModelClient
            systemInstruction: string option
            systemInstructionBuilder: PipeLineChain<AestasBot*StringBuilder> option
            contentLoadStrategy: BotContentLoadStrategy option
            contentParseStrategy: BotContentParseStrategy option
            messageReplyStrategy: BotMessageReplyStrategy option
            messageCacheStrategy: BotMessageCacheStrategy option
            contextStrategy: BotContextStrategy option
            inputPrefixBuilder: PrefixBuilder option
            userCommandPrivilege: (uint32*CommandPrivilege) list option
        }
        let inline createBot botParam =
            let bot = AestasBot()
            let ``set?`` (target: 't -> unit) = function Some x -> target x | None -> ()
            bot.Name <- botParam.name
            bot.Model <- Some botParam.model
            ``set?`` bot.set_SystemInstruction botParam.systemInstruction
            bot.SystemInstructionBuilder <- botParam.systemInstructionBuilder
            ``set?`` bot.set_ContentLoadStrategy botParam.contentLoadStrategy
            ``set?`` bot.set_MessageReplyStrategy botParam.messageReplyStrategy
            ``set?`` bot.set_MessageCacheStrategy botParam.messageCacheStrategy
            ``set?`` bot.set_ContentParseStrategy botParam.contentParseStrategy
            ``set?`` bot.set_ContextStrategy botParam.contextStrategy
            bot.PrefixBuilder <- botParam.inputPrefixBuilder
            match botParam.userCommandPrivilege with
            | Some x -> x |> List.iter (fun (uid, privilege) -> bot.CommandPrivilegeMap.Add(uid, privilege))
            | _ -> ()
            bot
        let inline createBotShort name model =
            let bot = AestasBot()
            bot.Name <- name
            bot.Model <- Some model
            bot