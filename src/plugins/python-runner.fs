//! nuget pythonnet=3.0.3
namespace Aestas.Plugins
open System
open System.Threading
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Text
open System.Text.Json
open Aestas.Core
open Aestas.AutoInit
open Aestas.Prim

open Python.Runtime

module PythonRunner =
    type PythonProfile = {pythonPath: string; pythonHome: string}
    type PythonRunner() =
        static let mutable _isinit = false
        static let _initPy profile =
            if not _isinit then
                Logger.logInfo["python"] "python initializing.."
                Logger.logWarn["python"] "This plugin may not be safe, use your own sandbox or docker to avoid risks."
                Runtime.PythonDLL <- profile.pythonPath
                PythonEngine.PythonHome <- profile.pythonHome
                PythonEngine.Initialize()
                PythonEngine.BeginAllowThreads() |> ignore
                _isinit <- true
        static let run code callback =
            let thread = new Thread(fun () -> (
                try
                    // sleep 2s, let the bot send their message first
                    Thread.Sleep 2000
                    use gil = Py.GIL()
                    use scope = Py.CreateScope()
                    let io = scope.Import("io", "io")
                    let sys = scope.Import("sys", "sys")
                    scope.Set("___redirect_stdio", io.GetAttr("StringIO").Invoke()) |> ignore
                    sys.SetAttr("stdout", scope.Get("___redirect_stdio"))
                    let ast = PythonEngine.Compile(code, "", RunFlagType.File)
                    scope.Execute(ast) |> ignore
                    let pyPrint = 
                        scope.Get("___redirect_stdio").GetAttr("getvalue").Invoke().As<string>()
                        |> sprintf "<runpy: output>:\n%s"
                        |> AestasText |> List.singleton
                    callback pyPrint
                    Logger.logInfo["python"] "python code executed"
                with e -> 
                    let pyPrint = e.Message |> AestasText |> List.singleton
                    callback pyPrint
                    Logger.logError["python"] e.Message
            ))
            thread.Start()
        interface IAutoInit<string*ContentParser*(AestasBot -> StringBuilder -> unit), unit> with
            static member Init _ = 
                "runpy"
                , fun bot domain params' content ->
                    match bot.TryGetExtraData("python") with
                    | Some (:? PythonProfile as profile) -> 
                        _initPy profile
                    | _ -> ()
                    if _isinit then
                        let code = content.Trim()
                        Logger.logInfo["python"] code
                        (fun pyPrint ->
                            Some pyPrint |> bot.SelfTalk domain |> Async.Ignore |> Async.Start
                            domain.Send ignore pyPrint |> Async.Ignore |> Async.Start
                        ) |> run code
                        sprintf "<runpy: code>\n```python\n%s\n```" code |> AestasText |> Ok
                    else Error "Python not initialized"
                , fun bot builder ->
                    builder.AppendLine "You may run python code use format like #[runpy:code]." |> ignore
                    builder.AppendLine "If you want to compute something complex, you can use this plugin." |> ignore
                    builder.AppendLine """e.g. #[runpy: print("helloworld")]""" |> ignore
                    builder.AppendLine "Code can be multi-line." |> ignore
                    builder.Append "io besides print, web request is not permitted." |> ignore