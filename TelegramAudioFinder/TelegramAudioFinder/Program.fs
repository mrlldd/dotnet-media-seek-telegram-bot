module TelegramAudioFinder.Program

open System
open System.Threading
open Newtonsoft.Json
open Suave
open Filters
open Successful
open Operators
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open TelegramAudioFinder
open Utils
open TelegramBot
open Restful

let isCtrlC (consoleKeyInfo: ConsoleKeyInfo) =
    consoleKeyInfo.Key = ConsoleKey.C
    && consoleKeyInfo.Modifiers = ConsoleModifiers.Control

let rec untilCloseCombination closeCombination =
    if not (Console.ReadKey true |> closeCombination)
    then untilCloseCombination closeCombination

let defaultHandler (r: HttpRequest) =
    OK(String.Format("well met {0}", r.path))

let telegramEndpoint = "/api/telegram"

let handler (update: Update) =

    bot.AnswerInlineQueryAsync(update.InlineQuery.Id, [])
    |> Async.AwaitTask
    |> ignore

    OK String.Empty

let startupAsync (cts: CancellationTokenSource) =
    let conf =
        { defaultConfig with
              cancellationToken = cts.Token }

    printfn "Starting up..."

    printfn "%s" (json (applicationConfig, Formatting.Indented))

    let telegramConfig = applicationConfig.Telegram

    let webPath =
        choose [ path "/" >=> (OK "Bruh, here we go again")
                 path telegramEndpoint
                 >=> POST
                 >=> restful handler
                 request defaultHandler ]

    let res = startWebServerAsync conf webPath

    let url =
        sprintf "%s%s" telegramConfig.WebhookUrl telegramEndpoint

    printfn "Setting up webhook: %s" url

    bot.SetWebhookAsync
        (url, maxConnections = telegramConfig.MaxConnections, allowedUpdates = [ UpdateType.InlineQuery ])
    |> Async.AwaitTask
    |> ignore

    printfn "Set webhook: %s" url
    res


[<EntryPoint>]
let main _ =
    let cts = new CancellationTokenSource()

    let _, server = startupAsync cts

    Async.Start(server, cts.Token)
    untilCloseCombination isCtrlC
    printfn "Shutting down..."
    cts.Cancel(false)
    0
