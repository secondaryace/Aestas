namespace Aestas
open System
open System.Collections.Generic
open Aestas
open Aestas.Core
open Aestas.Core.Logger
open Aestas.Prim

module Cli =
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
        type DynamicCursorView<'t>(object: unit -> 't, drawCursor: DrawCursor<'t>, posX: int, posY: int, width: int, height: int) =
            inherit CliView(posX, posY, width, height)
            override this.Draw() =
                drawCursor (object()) struct(this.X, this.Y, this.X+width, this.Y+height) |> ignore
        type DynamicObjectView<'t when 't :> CliView>(object: unit -> 't, posX: int, posY: int, width: int, height: int) =
            inherit CliView(posX, posY, width, height)
            override this.Draw() =
                let toDraw = object()
                this.Append toDraw
                base.Draw()
                this.Remove toDraw
            override this.HandleInput info =
                let toDraw = object()
                this.Append toDraw
                base.HandleInput info
                this.Remove toDraw
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
                        let rec trim mem =
                            if measureString mem < leftPanelWidth then mem
                            else trim (mem.Slice 1)
                        trim name |> Console.Write
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
    module Program =
        open Console
        [<EntryPoint>]
        let main(args) =
            AutoInit.initAll()
            Console.CursorVisible <- false
            logInfof[0] "Console Started with args %A" args 
            logTrace[0] "Test0"
            logDebug[0] "Test1"
            logInfo[0] "Test2"
            logWarn[0] "Test3"
            logError[0] "Test4"
            logFatal[0] "Test5"
            let panel = PanelView(0, 0, Console.WindowWidth, Console.WindowHeight)
            TextView("Aestas", None, Console.WindowWidth-6, 0) |> panel.Append
            TextView("[TAB]", Some ConsoleColor.DarkGray, 21, 0) |> panel.Append
            let mutable cachedLogOwners = getLoggerOwners()
            let initLogsTab() = 
                TabView(
                    cachedLogOwners
                    |> Array.map (
                        fun o -> 
                            toString o, 
                            VerticalListView(getLogs o, logEntryDrawCursor, ConsoleKey.UpArrow, ConsoleKey.DownArrow, 
                            0, 0, Console.WindowWidth, Console.WindowHeight-4) :> CliView)
                    |> arrList,
                    ConsoleKey.LeftArrow, ConsoleKey.RightArrow, 0, 0, Console.WindowWidth, Console.WindowHeight-2)
            let mutable logsTab = initLogsTab()
            let updateLogsTab() =
                let newLogOwners = getLoggerOwners()
                if cachedLogOwners.Length <> newLogOwners.Length then
                    cachedLogOwners <- newLogOwners
                    logsTab <- initLogsTab()
            let tab = TabView(arrList [|
                " Bots ", VerticalTabView(
                    AutoInit.bots
                    |> arrList
                    |> ArrList.map (fun b -> 
                        b.Name,
                        let p = PanelView(0, 0, Console.WindowWidth-13, Console.WindowHeight-2)
                        TextView($"""Model: {match b.Model with | Some model -> model.GetType().Name | None -> "None"}""", Some ConsoleColor.Gray, 0, 0)
                        |> p.Append
                        DynamicCursorView(
                            (fun () -> $"ReplyStrategy:          {b.MessageReplyStrategy}"), 
                            (fun s st -> setColor ConsoleColor.Gray; let ret = stringDrawCursor s st in resetColor(); ret),
                            0, 1, Console.WindowWidth-13, 1) |> p.Append
                        let mutable repStra = b.MessageReplyStrategy
                        ButtonView((
                            fun active ->
                                if active then b.MessageReplyStrategy <- repStra; false
                                else b.MessageReplyStrategy <- StrategyReplyNone; true
                            ),
                            "([M]ute)", Some ConsoleColor.DarkGray, Some ConsoleColor.Cyan, ConsoleKey.M, 15, 1) |> p.Append
                        TextView("Domains:", Some ConsoleColor.Gray, 0, 2) |> p.Append
                        TextView("([J]↓,[K]↑)", Some ConsoleColor.DarkGray, 9, 2) |> p.Append
                        VerticalListView(b.Domains.Values |> Array.ofSeq, 
                            (fun this t -> stringDrawCursor $"{this.DomainId}: {this.Name}" t), 
                            ConsoleKey.K, ConsoleKey.J, 4, 3, Console.WindowWidth-17, 5)
                        |> p.Append
                        TextView("Commands:", Some ConsoleColor.Gray, 0, 8) |> p.Append
                        TextView("([Z]↓,[X]↑)", Some ConsoleColor.DarkGray, 10, 8) |> p.Append
                        VerticalListView(b.CommandExecuters |> Array.ofSeq |> Array.map (fun c -> $"{c.Key}: {c.Value.GetType().Name}"), 
                            (stringDrawCursor), 
                            ConsoleKey.X, ConsoleKey.Z, 4, 9, Console.WindowWidth-17, 5)
                        |> p.Append
                        p),
                    ConsoleKey.UpArrow, ConsoleKey.DownArrow, 0, 0, Console.WindowWidth, Console.WindowHeight-2, 13) :> CliView
                " Chat ", VerticalTabView(
                    ConsoleBot.singleton.BindedBots
                    |> ArrList.map (fun b -> 
                        b.Name,
                        let p = PanelView(0, 0, Console.WindowWidth-13, Console.WindowHeight-2)
                        InputView((fun s -> ConsoleBot.singleton.Send(b, s)),0, Console.WindowHeight, Console.WindowWidth-13, 1) |> p.Append
                        TextView("Context:", Some ConsoleColor.Gray, 0, 0) |> p.Append
                        TextView("([J]↓,[K]↑)", Some ConsoleColor.DarkGray, 9, 0) |> p.Append
                        VerticalListView(ConsoleBot.singleton.BotContext b, stringDrawCursor, 
                            ConsoleKey.K, ConsoleKey.J, 0, 1, Console.WindowWidth-13, Console.WindowHeight-4)
                        |> p.Append
                        p),
                    ConsoleKey.UpArrow, ConsoleKey.DownArrow, 0, 0, Console.WindowWidth, Console.WindowHeight-2, 13) :> CliView
                " Logs ", DynamicObjectView(
                    (fun () -> logsTab), 0, 0, Console.WindowWidth, Console.WindowHeight-2) :> CliView
            |], ConsoleKey.BrowserBack, ConsoleKey.Tab, 0, 0, Console.WindowWidth, Console.WindowHeight)
            tab |> panel.Append
            let mutable draw = false
            let drawUi() =
                if draw then () else
                draw <- true
                try
                    if tab.Index = 2 then updateLogsTab()
                    clear()
                    panel.Draw()
                with ex ->
                    logError["Cli"] $"{ex}"
                draw <- false
            onLogUpdate.Add (fun _ -> if tab.Index = 2 then drawUi())
            ConsoleBot.singleton.ConsoleHook <- (fun _ -> if tab.Index = 1 then drawUi())
            let rec goForever() =
                drawUi()
                Console.ReadKey() |> panel.HandleInput
                goForever()
            goForever()
            
            //getLoggerOwners() |> Array.iter (fun o -> getLogsCopy o |> Seq.iter (fun x -> x.Print()))
            //
            0
