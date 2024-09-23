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

module TextToImage =
    type ImageResolutions = {resolutions: Dictionary<string, int*int>}
    let makeTextToImageParser textToImgMethod: (ContentParser*string*(AestasBot -> StringBuilder -> unit)) =
        fun bot domain params' content ->
            match bot.TryGetExtraData("imageResolutions"), params' with
            | Some (:? ImageResolutions as ress), ["res", res] when ress.resolutions.ContainsKey res -> 
                let res = ress.resolutions[res]
                let argument = {
                    prompt = content
                    negative = ""
                    resolution = res
                    seed = None
                }
                Logger.logInfo[0] (sprintf "t2i: %A" argument)
                match textToImgMethod argument |> Async.RunSynchronously with
                | Ok bytes ->
                    AestasImage(bytes, "image/png", fst res, snd res) |> Ok
                | Error e -> Error e
            | Some (:? ImageResolutions), _ -> Error $"Couldn't find resolution {content}"
            | _ -> Error "Couldn't find imageResolutions data"
        , "text2img"
        , fun bot sb ->
            sb.AppendLine("You may 'draw' picture by use format like #[text2img@res=resolution:prompt].") |> ignore
            sb.AppendLine("e.g. #[text2img@res=portrait:a beautiful anime girl, best quality].") |> ignore
            match bot.TryGetExtraData("imageResolutions") with
            | Some (:? ImageResolutions as ress) -> 
                sb.AppendLine("Available resolutions: ") |> ignore
                ress.resolutions |> Dict.iter (fun k v -> sb.Append(k).Append ' ' |> ignore)
            | _ -> ()