module TelegramAudioFinder.Startup

open System
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Telegram.Bot.Types.Enums
open TelegramAudioFinder
open TelegramAudioFinder.Utils
open TelegramAudioFinder.Restful
open TelegramAudioFinder.TelegramBot
open Newtonsoft.Json

let telegramEndpoint = "/api/telegram"

let defaultHandler (r: HttpRequest) =
    OK(String.Format("well met {0}", r.path))

let startupAsync cancellationToken =
    async {
        let conf =
            { defaultConfig with
                  cancellationToken = cancellationToken }

        printfn "Starting up..."
        printfn "%s" ((applicationConfig, Formatting.Indented) |> json)

        let telegramConfig = applicationConfig.Telegram

        let webPath =
            choose [ path "/" >=> (OK "Bruh, here we go again")
                     path telegramEndpoint
                     >=> POST
                     >=> (cancellationToken |> updateAsyncHandler |> restful)
                     request defaultHandler ]

        let url =
            sprintf "%s%s" telegramConfig.WebhookUrl telegramEndpoint

        printfn "Setting up webhook: %s" url

        do! bot.SetWebhookAsync
                (url, maxConnections = telegramConfig.MaxConnections, allowedUpdates = [ UpdateType.InlineQuery ])
            |> Async.AwaitTask
            |> Async.Ignore

        printfn "Set webhook: %s" url
        return startWebServerAsync conf webPath 
    }
