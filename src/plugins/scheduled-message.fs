namespace Aestas.Plugins
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Text
open System.Text.Json
open Aestas.Core
open Aestas.AutoInit
open Aestas.Prim

module ScheduledMessage =
    let makeScheduledMessageParser(): ContentParser*string*(AestasBot -> StringBuilder -> unit)  =
        fun bot domain params' content ->
            let dispatch hour min =
                Logger.logInfo[0] (sprintf "ScheduledMessage: %dh %dmin -> %s" hour min content)
                async {
                    do! Async.Sleep (hour * 3600000 + min * 60000)
                    do! 
                        sprintf "#[Alarm: %s]" content |> AestasText
                        |> List.singleton |> Some 
                        |> bot.SelfTalk domain |> Async.Ignore
                    Logger.logInfo[0] (sprintf "ScheduledMessage %s sended" content)
                } |> Async.Start
                Ok AestasBlank
            match params' with
            | ["afterhour", hour; "aftermin", min] -> dispatch (int hour) (int min)
            | ["afterhour", hour] -> dispatch (int hour) 0
            | ["aftermin", min] -> dispatch 0 (int min)
            | _ -> Error "Invalid parameters"
        , "remind-me"
        , fun bot sb ->
            sb.AppendLine("You may use remind-me to set a alarm for yourself, like #[remind-me@afterhour=hour@aftermin=minute:content].") |> ignore
            sb.Append("e.g. #[remind-me@afterhour=1@aftermin=10:Say good morning to everyone].") |> ignore