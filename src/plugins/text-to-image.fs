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
    let textToImageParser textToImgMethod: (ContentParser*string*(AestasBot -> StringBuilder -> unit)) =
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
            sb.AppendLine("e.g. #[text2img@res=portrait:Create an anime-style character with silver hair that has a blue gradient at the tips. The character should have two long strands of hair in the front and the rest tied up with a blue bow at the back. The character wears a navy blue dress with gold star patterns, white cuffs, and a white collar with a red ribbon. Add a moon-shaped accessory on top of the head. The character should be wearing thigh-high white socks and dark blue shoes with straps.].") |> ignore
            sb.AppendLine("e.g. #[text2img@res=portrait:Create an Semi-Painterly character with pink hair and green eyes. The character should have long hair that flows down the shoulders. The character wears a red and white outfit with a sailor collar, a yellow bow at the waist, and black shoes with white socks. The character is holding a staff with a heart-shaped top.].") |> ignore
            match bot.TryGetExtraData("imageResolutions") with
            | Some (:? ImageResolutions as ress) -> 
                sb.AppendLine("Available resolutions: ") |> ignore
                ress.resolutions |> Dict.iter (fun k v -> sb.Append(k).Append ' ' |> ignore)
            | _ -> ()