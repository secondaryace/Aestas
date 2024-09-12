module rec Aestas.Core
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Linq
open System.Reflection
open FSharpPlus
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
type ContentParser = Result<AestasContent, string> ContentCtor
type ProtocolSpecifyContentCtor = Result<IProtocolSpecifyContent, string> ContentCtor
type SystemInstructionBuilder = AestasBot -> StringBuilder -> unit
type PrefixBuilder = AestasBot -> AestasMessage -> AestasMessage
/// Bot class used in Aestas Core
/// Create this directly
type AestasBot() =
    let groups = AestasChatDomains()
    let extraData = Dictionary<string, obj>()
    let commandExecuters = Dictionary<string, CommandExecuter>()
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
    member val MemberCommandPrivilege: Dictionary<uint32, CommandPrivilege> = Dictionary() with get
    member val ContentParsers: Dictionary<string, struct(ContentParser*(AestasBot -> StringBuilder -> unit))> = Dictionary() with get
    member val ProtocolContentCtorTips: Dictionary<string, struct(ProtocolSpecifyContentCtor*(AestasBot->string))> = Dictionary() with get
    member val SystemInstructionBuilder: PipeLineChain<AestasBot*StringBuilder> option = None with get, set
    member val PrefixBuilder: PrefixBuilder option = None with get, set
    member _.ExtraData = extraData
    member _.TryGetExtraData key =
        if extraData.ContainsKey key then Some extraData[key] else None
    member this.AddExtraData (key: string) (value: obj) = 
        if extraData.ContainsKey key then failwith "Key already exists"
        else extraData.Add(key, value)
    member _.CommandExecuters = commandExecuters
    member _.TryGetCommandExecuter key = 
        if commandExecuters.ContainsKey key then Some commandExecuters[key] else None
    member this.AddCommandExecuter key value =
        if value.GetType().BaseType.GetGenericTypeDefinition() <> typedefof<CommandExecuter<_>> 
        then failwith "Not a command executer" 
        else
            if commandExecuters.ContainsKey key then failwith "Key already exists"
            else commandExecuters.Add(key, value)
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
            match this.Model, message.TryGetCommand this.CommandExecuters.Keys with
            | _ when groups.ContainsValue domain |> not -> return Error "This domain hasn't been added to this bot", ignore
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
    member _.Commands: arrList<'t> = arrList commands
    default this.AddCommand command = this.Commands.Add command
type TextToImageArgument ={
    prompt: string
    negative: string
    resolution: int*int
    seed: int option
    }
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
        member _.TryGetCommand prefixs =
            prefixs
            |> Seq.tryFind (fun prefix ->
                match message.content with
                | AestasText s::_ when s.StartsWith prefix -> 
                    true
                | _ -> false)
            |> Option.map (fun prefix ->
                match message.content with
                | AestasText s::_ -> 
                    prefix, s.Substring(prefix.Length)
                | _ -> prefix, "")
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
                else if bot.ContentParsers.ContainsKey funcName then
                    match fst' bot.ContentParsers[funcName] bot domain param content with
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
        bot.ContentParsers |> Dict.iter (fun name struct(ctor, tipBuilder) -> 
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
    type SpacedTextCommand = {
        name: string
        description: string
        accessibleDomain: CommandAccessibleDomain
        privilege: CommandPrivilege
        execute: SpacedTextCommandExecuter -> CommandEnvironment -> string[] -> unit
    }
    type SpacedTextCommandExecuter(commands) =
        inherit CommandExecuter<SpacedTextCommand>(commands)
        override this.Execute env cmd = 
            let cmd = Regex.Matches(cmd, """("[^\n\r]+"|[^\n\r ]+)""")
            if cmd.Count = 0 then env.log "No command found"
            else
                let cmd = 
                    cmd |> Array.ofSeq 
                    |> Array.map (fun x -> if x.Value.StartsWith("\"") then x.Value.[1..^1] else x.Value)
                let name = cmd[0]
                let args = cmd |> Array.skip 1
                match this.Commands |> ArrList.tryFind (fun c -> c.name = name) with
                | Some cmd when cmd.privilege <= env.privilege -> cmd.execute this env args
                | Some _ -> env.log $"Permission denied"
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
            ArrList.iter (fun v -> sb.Append $"\n* {v.name}:\n   {v.description}" |> ignore)
            sb.ToString() |> env.log
        }
    let commands() = [
            versionCommand()
            clearCommand()
            helpCommand()
        ]
module BotHelper =
    let inline bindDomain (bot: AestasBot) domain =
        bot.BindDomain domain
    let inline addExtraData (bot: AestasBot) (key: string) (value: obj) =
        bot.AddExtraData key value
    //let inline getPrimaryCommands() =
    //    (Builtin.operators.Values |> List.ofSeq) @ (Builtin.commands.Values |> List.ofSeq)
    let inline addCommandExecuter (bot: AestasBot) key executer =
        bot.AddCommandExecuter key executer
    let inline addContentParser (bot: AestasBot) (ctor: ContentParser, name: string, tip: AestasBot -> StringBuilder -> unit) =
        bot.ContentParsers.Add(name, (ctor, tip))
    let inline addProtocolContentCtorTip (bot: AestasBot) (ctor: ProtocolSpecifyContentCtor, name: string, tip: AestasBot -> string) =
        bot.ProtocolContentCtorTips.Add(name, (ctor, tip))
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
    let builtinCommandsExecuter() = Builtin.commands() |> Builtin.SpacedTextCommandExecuter
    let makeExecuterWithBuiltinCommands list = Builtin.commands() @ list |> Builtin.SpacedTextCommandExecuter
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
        | Some x -> x |> List.iter (fun (uid, privilege) -> bot.MemberCommandPrivilege.Add(uid, privilege))
        | _ -> ()
        bot
    let inline createBotShort name model =
        let bot = AestasBot()
        bot.Name <- name
        bot.Model <- Some model
        bot