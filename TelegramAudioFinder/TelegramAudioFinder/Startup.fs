module TelegramAudioFinder.Startup

open System
open Suave
open Filters
open Operators
open Successful
open Telegram.Bot.Types.Enums
open TelegramAudioFinder
open Utils
open Restful
open TelegramBot
open Newtonsoft.Json

let telegramEndpoint = "/api/telegram"

let defaultHandler (r: HttpRequest) =
    OK(String.Format("well met {0}", r.path))

let startupAsync cancellationToken =
    let conf =
        { defaultConfig with
              cancellationToken = cancellationToken }

    printfn "Starting up..."
    printfn "%s" (json (applicationConfig, Formatting.Indented))

    let telegramConfig = applicationConfig.Telegram

    let webPath =
        choose [ path "/" >=> (OK "Bruh, here we go again")
                 path telegramEndpoint
                 >=> POST
                 >=> (cancellationToken |> updateAsyncHandler |> restful)
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
