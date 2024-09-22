namespace Aestas.Bots
// first line is the namespace, most of the time is Aestas.Bots
// then open necessary namespaces
open System
open System.Collections.Generic
open System.Linq
open Aestas.Prim
open Aestas.Core
open Aestas.Core.AestasBot
open Aestas.AutoInit
// open the adapter you want to use
//open Aestas.Adapters.AestasLagrangeBot
// open the model you want to use
open Aestas.Llms.Gemini
// open module of the plugins you want to use
open Aestas.Plugins.MsTts
open Aestas.Plugins.MsBing
open Aestas.Plugins.Sticker
// open commands namespace if you want to use commands
open Aestas.Commands
// there are many CommandExecuters, AestasScriptExecuter for example
open Aestas.Commands.AestasScript

type TemplateBot =
    interface IAutoInit<AestasBot, unit> with
        static member Init _ =
            // initialize the model manually
            let model = GeminiLlm({
                apiKey = tryGetEnv "GEMINI_API_KEY"
                gcloudPath = tryGetEnv "GCLOUD_PATH"
                safetySettings = [|
                    {category = "HARM_CATEGORY_HARASSMENT"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_HATE_SPEECH"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_SEXUALLY_EXPLICIT"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_DANGEROUS_CONTENT"; threshold = "BLOCK_SOME"}
                |]
                generation_configs = {
                    defaultGenerationConfig with
                        temperature = Some 1.
                        maxLength = Some 4096
                        topK = Some 64
                        topP = Some 1.
                } |> Some
                }, 
                "gemini-1.5-flash")
            let systemInstruction = """
Your system instruction here
"""
            let bot = 
                createBot {|
                    name = "Template"// modify there to your bot's name
                    model = model
                    systemInstruction = systemInstruction |> Some
                    systemInstructionBuilder = Builtin.buildSystemInstruction |> PipeLineChain |> Some
                    friendStrategy = None
                    contentLoadStrategy = StrategyLoadOnlyMentionedOrPrivate |> Some
                    contentParseStrategy = None
                    messageReplyStrategy = StrategyReplyOnlyMentionedOrPrivate |> Some
                    messageCacheStrategy = None
                    contextStrategy = StrategyContextTrimWhenExceedLength 300 |> Some
                    // [Name|Time] will be inserted at the beginning of each sentence
                    inputPrefixBuilder = Builtin.buildPrefix |> Some
                    userCommandPrivilege = [
                        0u, CommandPrivilege.High
                    ] |> Some
                |}
            // bind to QQ 
            //getProtocol<LagrangeAdapter>().InitDomainView bot 0u(* QQ *) |> bindDomain bot
            // bind to Console
            ConsoleBot.singleton.InitDomainView(bot, 0u) |> bindDomain bot
            // try to bind to WebUI
            tryGetProtocol "WebUIAdapter" 
            |> Option.map (fun p -> p.InitDomainView bot 2u |> bindDomain bot)
            |> ignore
            addCommandExecuters bot [
                "/", makeExecuterWithBuiltinCommands []
                "#", AestasScriptExecuter [
                    AestasScriptCommands.version()
                    AestasScriptCommands.clear()
                    AestasScriptCommands.help()
                    RegenerateCommand.make()
                    TodaysDietCommand.make()
                    InfoCommand.make()
                ]
                ">", ObsoletedCommand.ObsoletedCommandExeuter [
                    ObsoletedCommands.commands()
                ]
            ]
            // these plugins could be removed
            addContentParsers bot [
                getContentParser<MsTtsParser>()
                getContentParser<MsBingFunction>()
                getContentParser<StickerParser>()
            ]
            // these files don't exist, just examples
            // replace stickers with your own stickers

            // addExtraData bot "stickers" {
            //     stickers = dict' [
            //         "欸嘿", {path = "media/stickers/chuckle.png"; width = 328; height = 322}
            //         "呜呜呜", {path = "media/stickers/cry.png"; width = 328; height = 322}
            //     ]
            // }
            addExtraData bot "mstts" {
                subscriptionKey = "Your subscription key here"
                subscriptionRegion = "Your subscription region here"
                voiceName = "zh-CN-XiaoyiNeural"
                outputFormat = "ogg-24khz-16bit-mono-opus"
                styles = []
                }
            addExtraData bot "msbing" {
                subscriptionKey = "Your subscription key here"
                baseUri = "https://api.bing.microsoft.com/v7.0/search"
                mkt = "zh-CN"
            }
            bot