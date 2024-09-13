namespace Aestas
open System
open Aestas
open Aestas.Core
open Aestas.Llms.Gemini
open Aestas.Core.Logger
open Aestas.Prim
open Aestas.Core.Console
open Aestas.Plugins.MsBing
module Cli =
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

//         Console.ReadLine() |> ignore
//         // let console = ConsoleBot.ConsoleAdapter() :> IProtocolAdapter
//         // console.Run() |> Async.RunSynchronously
//         0
//         let printHelp() =
//             printfn "Usage: Aestas [command]"
//             printfn "Commands:"
//             printfn "listen [token] [selfId] [selfName] [host] - Listen to the server"
//             printfn "run - Chat in the console directly"
//         let run() =
//             let introHelp = """----------------------------------------------
//     Aestas - A Simple Chatbot Client
//     Type #exit to exit
//     Type #help to show this again
//     Type #commands to show all commands
//     Voice and Image are not supported in cli
// ----------------------------------------------
// """
//             let mutable client = ErnieClient("profiles/chat_info_private_ernie.json", Ernie_35P) :> IChatClient
//             printfn "%s" introHelp
//             let rec mainLoop() =
//                 let rec printDialogs (messages: ResizeArray<Message>) i =
//                     if i = messages.Count then () else
//                     printfn "%s: %s" messages.[i].role messages.[i].content
//                     printDialogs messages (i+1)
//                 printf "> "
//                 let s = Console.ReadLine()
//                 if s.StartsWith '#' then
//                     let command = s[1..].ToLower().Split(' ')
//                     match command[0] with
//                     | "exit" -> ()
//                     | "help" -> 
//                         printfn "%s" introHelp
//                         mainLoop()
//                     | "commands" ->
//                         printfn """Commands:
// #exit - Exit the program
// #help - Show the intro help
// #commands - Show all commands
// #current - Show current model
// #ernie [model=chara|35|40|35p|40p] - Change model to ernie
// #gemini [model=15|10] - Change model to gemini
// #cohere - Change model to cohere"""
//                         mainLoop()
//                     | "current" ->
//                         printfn $"Model is {client.GetType().Name}"
//                         mainLoop()
//                     | "ernie" ->
//                         if command.Length < 2 then 
//                             printfn "Usage: ernie [model=chara|35|40|35p|40p]"
//                             mainLoop()
//                         else
//                             let model = 
//                                 match command[1] with
//                                 | "chara" -> Ernie_Chara
//                                 | "35" -> Ernie_35
//                                 | "40" -> Ernie_40
//                                 | "35p" -> Ernie_35P
//                                 | "40p" -> Ernie_40P
//                                 | _ -> 
//                                     command[1] <- "default:chara"
//                                     Ernie_Chara
//                             client <- ErnieClient("profiles/chat_info_private_ernie.json", model)
//                             printfn $"Model changed to ernie{command[1]}"
//                             mainLoop()
//                     | "gemini" -> 
//                         if command.Length < 2 then 
//                             printfn "Usage: gemini [model=15|10]"
//                             mainLoop()
//                         else
//                             match command[1] with
//                             | "15" -> client <- GeminiClient ("profiles/chat_info_private_gemini.json", false) :> IChatClient
//                             | "f" -> client <- GeminiClient ("profiles/chat_info_private_gemini.json", true) :> IChatClient
//                             | "10" -> client <- Gemini10Client ("profiles/chat_info_private_gemini.json", "") :> IChatClient
//                             | _ -> 
//                                 command[1] <- "default:10"
//                                 client <- Gemini10Client ("profiles/chat_info_private_gemini.json", "") :> IChatClient
//                             printfn $"Model changed to gemini {command[1]}"
//                             mainLoop()
//                     | "cohere" ->
//                         client <- CohereClient("profiles/chat_info_private_cohere.json")
//                         printfn $"Model changed to cohere"
//                         mainLoop()
//                     | _ -> 
//                         printfn "Unknown command."
//                         mainLoop()
//                 elif s.Length > 400 then
//                     printfn "Input should less than 400 characters."
//                     mainLoop()
//                 elif s <> "" then
//                     //client.PostOrAppend s
//                     //client.CheckDialogLength()
//                     //client.Receive(fun _ -> async {return ()})
//                     client.Turn s (fun _ -> async {return ()})
//                     Console.Clear()
//                     printDialogs client.Messages 0
//                     mainLoop()
//                 else
//                     mainLoop()
//             mainLoop()
//         if args.Length = 0 then
//             printHelp()
//         else
//             match args[0] with
//             | "run" ->
//                 AestasBot.run()
//             | "cli" ->
//                 run()
//                 ()
//             | _ ->
//                 printHelp()
//         0