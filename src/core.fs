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
                    
        let private logs = Dictionary<obj, LogEntry arrList>()
        let onLogUpdate = arrList<LogEntry -> unit>()
        let getLoggerOwners() = logs.Keys |> Seq.toArray
        let getLogs o = logs[o] :> IReadOnlyList<LogEntry>
        let inline private log' (key: obj) (lv: LogLevel) (s: string) =
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
        let inline private log'' (lv: LogLevel) (key: obj) (s: string) = log' key lv s
        let log = IndexerBox<obj, LogLevel -> string -> unit> log'
        let logTrace = IndexerBox<obj, string -> unit> (log'' LogLevel.Trace)
        let logDebug = IndexerBox<obj, string -> unit> (log'' LogLevel.Debug)
        let logInfo = IndexerBox<obj, string -> unit> (log'' LogLevel.Info)
        let logWarn = IndexerBox<obj, string -> unit> (log'' LogLevel.Warn)
        let logError = IndexerBox<obj, string -> unit> (log'' LogLevel.Error)
        let logFatal =  IndexerBox<obj, string -> unit> (log'' LogLevel.Fatal)
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
        abstract member InitDomainView: AestasBot*uint32 -> AestasChatDomain
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
        abstract member Send: AestasContent list -> Async<Result<IMessageAdapter, string>>
        abstract member Name: string with get
        abstract member OnReceiveMessage: IMessageAdapter -> Async<Result<IMessageAdapter, string>>
        default this.OnReceiveMessage msg = 
            async {
                match this.Bot with
                | None -> return Error "No bot binded to this domain"
                | Some bot -> 
                    match! bot.Reply this msg with
                    | Error e, _ -> return Error e
                    | Ok [], _ -> 
                        this.Messages.Add msg
                        return Ok msg
                    | Ok reply, callback -> 
                        match! reply |> this.Send with
                        | Error e -> return Error e
                        | Ok rmsg ->
                            this.Messages.Add msg
                            this.Messages.Add rmsg
                            rmsg.Parse() |> callback
                            return Ok msg

            }
    type AestasChatDomains = Dictionary<uint32, AestasChatDomain>
    type AestasBotContentLoadStrategy =
        | StrategyLoadNone
        | StrategyLoadAll
        | StrategyLoadByPredicate of (IMessageAdapter -> bool)
    type AestasBotReplyStrategy = 
        /// Won't reply to any message
        | StrategyReplyNone
        /// Only reply to messages that mentioned the bot or private messages
        | StrategyReplyOnlyMentionedOrPrivate
        /// Reply to all messages
        | StrategyReplyAll
        /// Reply to messages whose domain ID is in the whitelist
        | StrategyReplyByPredicate of (AestasMessage -> bool)
    type AestasBotContentParseStrategy =
        /// Parse all content to plain text
        | StrategyParseAllToPlainText
        /// Parse all content to AestasContent, ignore errors
        | StrategyParseAndIgnoreError
        /// Parse all content to AestasContent, alert errors like <no such function [{funcName}]>
        | StrategyParseAndAlertError
        /// Parse all content to AestasContent, parse errors format to plain text, like [func: content] -> content
        | StrategyParseAndDestructErrorFormat
    /// Used in Aestas.Core <-> Model
    /// Implement this interface to box any object to a AestasContent.
    /// For example, model returns [mstts@emotion=Cheerful: Hello], use this to store and convert is to AestasAudio
    type IAestasMappingContent =
        /// Content type of this content, like "mstts"
        abstract member ContentType: string with get
        /// Convert this to a normal AestasContent
        abstract member Convert: AestasBot -> AestasChatDomain -> AestasContent
    /// Used in Model <-> Aestas.Core <-> Protocol
    /// Provide extra content type. For example, market faces in QQ
    type IProtocolSpecifyContent =
        /// .NET type of this content
        abstract member Type: Type with get
        /// Content type of this content, like "stickers"
        abstract member ContentType: string with get
        abstract member ToPlainText: unit -> string
        /// Convert this to something that only protocol can understand
        abstract member Convert: AestasBot -> AestasChatDomain -> obj option
    type AestasChatMember = {uid: uint32; name: string}
    type AestasContent = 
        | AestasText of string 
        /// byte array, mime type, width, height
        | AestasImage of byte[]*string*int*int
        | AestasAudio of byte[]*string
        | AestasVideo of byte[]*string
        | AestasMention of AestasChatMember
        | AestasQuote of uint64
        | AestasFold of IMessageAdapterCollection
        | AestasMappingContent of IAestasMappingContent
        | ProtocolSpecifyContent of IProtocolSpecifyContent
    type AestasMessage = {
        sender: AestasChatMember
        content: AestasContent list
        mid: uint64
        }
    type InputConverterFunc = IMessageAdapterCollection -> AestasContent -> string
    /// domain * params * content
    type ContentParam = AestasChatDomain*list<string*string>*string
    type OverridePluginFunc = ContentParam -> AestasContent
    type MappingContentCtor = ContentParam -> IAestasMappingContent
    type ProtocolSpecifyContentCtor = ContentParam -> IProtocolSpecifyContent
    type SystemInstructionBuilder = AestasBot -> string -> string
    type PrefixBuilder = AestasBot -> AestasMessage -> AestasMessage
    /// Bot class used in Aestas Core
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
        member val ReplyStrategy = StrategyReplyNone with get, set
        member val ContentParseStrategy = StrategyParseAndAlertError with get, set
        member val Commands: Dictionary<string, ICommand> = Dictionary() with get
        member val CommandPrivilegeMap: Dictionary<uint32, CommandPrivilege> = Dictionary() with get
        member val MappingContentConstructors: Dictionary<string, MappingContentCtor*string> = Dictionary() with get
        member val ProtocolContentConstructors: Dictionary<string, ProtocolSpecifyContentCtor*string> = Dictionary() with get
        member val SystemInstructionBuilder: SystemInstructionBuilder option = None with get, set
        member val PrefixBuilder: PrefixBuilder option = None with get, set
        member _.ExtraData
            with get key =
                if extraData.ContainsKey key then Some extraData[key] else None
        member this.AddExtraData (key: string) (value: obj) = 
            if extraData.ContainsKey key then failwith "Key already exists"
            else extraData.Add(key, value)
        member this.SystemInstruction 
            with get() = 
                match this.SystemInstructionBuilder with
                | Some b -> b this originalSystemInstruction
                | None -> originalSystemInstruction
            and set value = originalSystemInstruction <- value
        member this.BindDomain (domain: AestasChatDomain) = 
            if groups.ContainsKey domain.DomainId then
                groups[domain.DomainId].Bot <- None
                groups[domain.DomainId] <- domain
            else groups.Add(domain.DomainId, domain)
            domain.Bot <- Some this
        member this.Reply (domain: AestasChatDomain) (message: IMessageAdapter) =
            async {
                match this.Model with
                | _ when groups.ContainsValue domain |> not -> return Error "This domain hasn't been added to this bot", ignore
                | _ when message.Command.IsSome ->
                    let command = message.Command.Value
                    Command.excecute {
                        bot = this; domain = domain
                        log = AestasText >> List.singleton >> domain.Send >> Async.Ignore >> Async.Start
                        privilege = 
                            if this.CommandPrivilegeMap.ContainsKey message.SenderId then
                                this.CommandPrivilegeMap[message.SenderId]
                            else CommandPrivilege.Normal
                        } command
                    return Ok [], ignore
                | None -> return Error "No model binded to this bot", ignore
                | Some model ->
                    let message = 
                        match this.ContentLoadStrategy with
                        | StrategyLoadNone -> message.ParseAsPlainText()
                        | StrategyLoadAll ->  message.Parse()
                        | StrategyLoadByPredicate p when p message -> message.Parse()
                        | _ -> message.ParseAsPlainText()
                    let message =
                        match this.PrefixBuilder with
                        | Some b -> b this message
                        | None -> message
                    match this.ReplyStrategy with
                    | StrategyReplyOnlyMentionedOrPrivate when
                        message.content |> List.exists 
                            (function 
                            | AestasMention m when m.uid = domain.Self.uid -> true 
                            | _ -> false) ->
                        return! model.Send(this, domain, message)
                    | StrategyReplyOnlyMentionedOrPrivate when domain.Private ->
                        return! model.Send(this, domain, message)
                    | StrategyReplyAll ->
                        return! model.Send(this, domain, message)
                    | StrategyReplyByPredicate p when p message ->
                        return! model.Send(this, domain, message)
                    | _ -> 
                        return Ok [], ignore
            }
        member this.SelfTalk (domain: AestasChatDomain) (content: AestasContent list) = 
            async {
                match this.Model with
                | _ when groups.ContainsValue domain |> not -> return Error "This domain hasn't been added to this bot"
                | None -> return Error "No model binded to this bot"
                | Some model ->
                    match! model.SendContent(this, domain, content) with
                    | Error e, _ -> return Error e
                    | Ok rmsg, callback -> 
                        match! rmsg |> domain.Send with
                        | Error e -> return Error e
                        | Ok rmsg ->
                            domain.Messages.Add rmsg
                            rmsg.Parse() |> callback
                            return Ok rmsg
            }
        member this.ClearCachedContext(domain: AestasChatDomain) = 
            match this.Model with
            | None -> ()
            | Some model -> model.ClearCache()
            domain.Messages.Clear()
    type GenerationConfig = {
        temperature: float
        max_length: int
        top_k: int
        top_p: float
        }
    type ILanguageModelClient =
        interface
            /// bot * domain * message -> response * callback, with a certain sender in AestasMessage
            abstract member Send: AestasBot*AestasChatDomain*AestasMessage -> Async<Result<AestasContent list, string>*(AestasMessage -> unit)>
            /// bot * domain * contents -> response * callback, with no sender
            abstract member SendContent: AestasBot*AestasChatDomain*(AestasContent list) -> Async<Result<AestasContent list, string>*(AestasMessage -> unit)>
            abstract member ClearCache: unit -> unit
        end
    type UnitClient() =
        interface ILanguageModelClient with
            member _.Send (bot, domain, message) = 
                async {
                    return Ok message.content, ignore
                }
            member _.SendContent (bot, domain, contents) =
                async {
                    return Ok contents, ignore
                }
            member _.ClearCache() = ()
        end
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
    type Atom =
        | AtomTuple of Atom list
        | Number of float32
        | String of string
        | Identifier of string
        | Unit
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
            override this.Send msgs = 
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
                    return (messages, self, s) |> ConsoleMessage :> IMessageAdapter |> Ok
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
                member this.InitDomainView(bot, domainId) = this.InitDomainView(bot, domainId)
        let singleton = ConsoleChat()
    module Command =
        module Lexer =
            type Token =
                | TokenSpace
                | TokenPrint
                | TokenFloat of single
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
                                    let (AValue t) = d['\000'] in tokens.Add t
                                    splitOp lp.operators i
                                else if i = cache.Length then ()
                                else tokens.Add (TokenError $"Unknown symbol {cache[if i = cache.Length then i-1 else i]}")
                            splitOp lp.operators 0
                            innerRec tokens cursor
                    | c when isNumber c ->
                        let (cursor, isFloat) = scanNumber lp cursor source cache false
                        let s = cache.ToString()
                        tokens.Add(TokenFloat (Single.Parse s))
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
                                let (ADict d) = d[s[0]]
                                d.Add('\000', AValue t)
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
                            let (ADict d) = d[s[0]]
                            addToDict (m.Slice 1) t d
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
                    let (Call args), tokens, errors = parseTupleItem r errors
                    Call (args.Head::l::args.Tail), tokens, errors
                | _ -> l, tokens, errors
            let rec parseExpr (tokens: Token list) (errors: string list) =
                let rec go tokens acc errors =
                    match tokens with
                    | TokenSpace::TokenPipe::_
                    | TokenSpace::TokenRightPipe::_
                    | TokenSpace::TokenRightRound::_ -> acc |> List.rev, tokens, errors
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
                // | Identifier "conslog" ->
                //     if args.Tail.Tail.IsEmpty |> not then env.log "To much arguments, try use tuple."; Unit else
                //     // let conslog = printfn "At %d %s" (if env.chain.GroupUin.HasValue then env.chain.GroupUin.Value else env.chain.FriendUin)
                //     // let result = excecuteAst {aestas = env.aestas; context = env.context; chain = env.chain; commands = env.commands; log = conslog; model = env.model } args.Tail.Head
                //     // conslog $"returns {result}"
                //     Unit
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
            let tokens = Lexer.scanWithoutMacro LanguagePack cmd
            let ast, _, errors = Parser.parse tokens []
            Logger.logInfo[0] <| sprintf "%A,%A,%A" tokens ast errors
            match errors with
            | [] -> 
                match excecuteAst env ast with
                | Unit -> ()
                | x -> env.log <| x.ToString()
            | _ -> env.log <| String.Join("\n", "Error occured:"::errors)
    module Builtin =
        // f >> g, AutoInit.inputConverters >> (Builtin.inputConverters messages)
        let rec modelInputConverters (c: IMessageAdapterCollection) = function
        | AestasText x -> x
        | AestasImage _ -> "#[image: not supported]"
        | AestasAudio _ -> "#[audio: not supported]"
        | AestasVideo _ -> "#[video: not supported]"
        | AestasMention x -> $"#[mention: {x.name}]"
        | AestasQuote x -> $"#[quote: {c.FirstOrDefault(fun y -> y.MessageId = x).Preview}]"
        | AestasFold x -> $"#[foldedMessage: {x.Count} messages]"
        | ProtocolSpecifyContent x -> x.ToPlainText()
        | _ -> failwith "Could not handle this content type"
        let overridePrimCtor = dict' [
            "text", fun (domain: AestasChatDomain, param: (string*string) list, content) -> AestasText content
            "image", fun (domain, param, content) -> AestasImage (Array.zeroCreate<byte> 0, "image/png", 0, 0)
            "audio", fun (domain, param, content) -> AestasAudio (Array.zeroCreate<byte> 0, "audio/mpeg")
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
                    sprintf "funcName: %s, param: %A, content: %s" funcName param content |> Logger.logInfo[0]
                    if overridePrimCtor.ContainsKey funcName then
                        let content = overridePrimCtor[funcName] (domain, param, content)
                        result.Add content
                    else if bot.MappingContentConstructors.ContainsKey funcName then
                        let content = fst bot.MappingContentConstructors[funcName] (domain, param, content)
                        content |> AestasMappingContent |> result.Add
                    else if bot.ProtocolContentConstructors.ContainsKey funcName then
                        let content = fst bot.ProtocolContentConstructors[funcName] (domain, param, content)
                        content |> ProtocolSpecifyContent |> result.Add
                    else
                        match bot.ContentParseStrategy with
                        | StrategyParseAndAlertError ->
                            $"<no such function [{funcName}]>" |> AestasText |> result.Add
                        | StrategyParseAndDestructErrorFormat ->
                            content |> AestasText |> result.Add
                        | _ -> ()
                        // log here
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
        let buildSystemInstruction (bot: AestasBot) (prompt: string) =
            let sb = StringBuilder()
            sb.Append "## 1.About yourself\n" |> ignore
            sb.Append prompt |> ignore
            sb.Append "## 2.Some functions for you\n" |> ignore
            overridePrimTip |> List.iter (fun (name, tips) -> sb.Append $"**{name}**:\n{tips}\n" |> ignore)
            bot.MappingContentConstructors |> Dict.iter (fun name (ctor, tips) -> sb.Append $"**{name}**:\n{tips}\n" |> ignore)
            bot.ProtocolContentConstructors |> Dict.iter (fun name (ctor, tips) -> sb.Append $"**{name}**:\n{tips}\n" |> ignore)
            sb.ToString()
        let buildPrefix (bot: AestasBot) (msg: AestasMessage) =
            {
                content = AestasText $"[{msg.sender.name}|{DateTime.Now:``yyyy-MM-dd HH:mm``}] "::msg.content
                sender = msg.sender
                mid = msg.mid
            }
        type MentionMappingContent(uid, name) =
            interface IAestasMappingContent with
                member _.ContentType = "mention"
                member _.Convert bot domain = 
                    AestasMention {uid = uid; name = name}
            static member Constructor (domain: AestasChatDomain, params': (string*string) list, content: string) = 
                match domain.Members |> Array.tryFind (fun x -> x.name = content) with
                | Some member' -> MentionMappingContent(member'.uid, member'.name)
                | None -> MentionMappingContent(0u, content)

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
                    env.log $"""## Domain:
* ID={env.domain.DomainId}
* Name={env.domain.Name}
* Private={env.domain.Private}"""; Unit
        type ListDomainCommand() =
            interface ICommand with
                member _.Name = "lsdomain"
                member _.Help = "List all domains"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    let sb = StringBuilder()
                    sb.Append "## Domain List" |> ignore
                    env.bot.Domains |>
                    Dict.iter (fun _ v -> sb.Append $"\n* {v.DomainId}: {v.Name}" |> ignore)
                    sb.ToString() |> env.log; Unit
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
                member _.Name = "help"
                member _.Help = """Dump the cached context
    Usage: dump [reverseIndexStart=count-1] [reverseIndexEnd=0]
"""
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal
                member _.Execute env args =
                    let msgs = env.domain.Messages
                    let s, t =
                        match args with
                        | Number x::[] -> msgs.Count-1-int x, msgs.Count-1
                        | Number x::Number y::[] -> msgs.Count-1-int x, msgs.Count-1-int y
                        | _ -> 0, msgs.Count-1
                    let sb = StringBuilder()
                    sb.Append "## Cached Context:" |> ignore
                    let rec go i =
                        if i > t then ()
                        else
                            let m = msgs[i].Parse()
                            sb.Append $"\n* {m.sender.name}:\n   {m.content}" |> ignore
                            go (i+1)
                    go s
                    sb.ToString() |> env.log; Unit
        type ClearCommand() =
            interface ICommand with
                member _.Name = "clear"
                member _.Help = "List all commands"
                member _.AccessibleDomain = CommandAccessibleDomain.All
                member _.Privilege = CommandPrivilege.Normal                
                member _.Execute env args =
                    env.bot.ClearCachedContext env.domain
                    env.log "Cached context cleared"; Unit
        let commands: Dictionary<string, ICommand> = dict' [
            "version", VersionCommand()
            "domaininfo", DomainInfoCommand()
            "lsdomain", ListDomainCommand()
            "help", HelpCommand()
            "dump", DumpCommand()
            "clear", ClearCommand()
        ]
        type BillionDollarBotClient() =
            interface ILanguageModelClient with
                member this.Send (bot, domain, message) = (this :> ILanguageModelClient).SendContent(bot, domain, message.content)
                member _.SendContent (bot, domain, contents) =
                    async {
                        let strs =  contents |> List.map (modelInputConverters domain.Messages)
                        strs |> String.concat " " |> modelOutputParser bot domain |> printfn "%A"
                        let response =
                            (strs |> String.concat " ")
                                .Replace("You ", "~!@#")
                                .Replace("I ", "You ")
                                .Replace("~!@#", "I ")
                                .Replace("you", "I ")
                                .Replace("?", "!")
                        return Ok [AestasText response], ignore
                    }
                member _.ClearCache() = ()