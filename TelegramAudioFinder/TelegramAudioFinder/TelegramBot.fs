module TelegramAudioFinder.TelegramBot

open System
open Google
open Google.Apis.Services
open Google.Apis.Util
open Google.Apis.YouTube.v3
open Google.Apis.YouTube.v3.Data
open Newtonsoft.Json
open Suave
open Successful
open Telegram.Bot
open Telegram.Bot.Types
open Telegram.Bot.Types.InlineQueryResults
open Utils
open FSharp.Data.Runtime.Caching

let bot =
    TelegramBotClient(applicationConfig.Telegram.Token)

let private cacheTimeout = TimeSpan.FromMinutes(1 |> float)

let private service =
    let youTubeConfig = applicationConfig.Services.Youtube
    let init = BaseClientService.Initializer()
    init.ApiKey <- youTubeConfig.Key
    init.ApplicationName <- youTubeConfig.AppName
    new YouTubeService(init)

let private curriedResultSender queryId offset items =
    bot.AnswerInlineQueryAsync
        (queryId,
         items,
         nextOffset = (offset |> string),
         cacheTime = cacheTimeout.Seconds,
         cancellationToken = cts.Token)
    |> Async.AwaitTask

let private youtubeItemsMapper (index, item: SearchResult) =
    let snippet = item.Snippet

    let url =
        sprintf "https://youtube.com/watch?v=%s" item.Id.VideoId

    let res =
        InlineQueryResultArticle(index.ToString(), snippet.Title, InputTextMessageContent(url))

    res.Description <- snippet.Description
    res.ThumbUrl <- snippet.Thumbnails.Default__.Url
    res.ThumbHeight <- 100
    res.ThumbWidth <- 100
    res :> InlineQueryResultBase

let private optional s =
    if String.IsNullOrEmpty(s) then None else Some s

let private pageLength = 10

let private youtubeContinuationTokensCache = createInMemoryCache cacheTimeout

let private sendInlineQueryResultAsync (queryId, offset: int, keyFactory, youtubeResponse: SearchListResponse) =
    youtubeContinuationTokensCache.Set ((offset |> keyFactory), youtubeResponse.NextPageToken)

    printfn "%s" (json (youtubeResponse, Formatting.Indented))

    youtubeResponse.Items
    |> Seq.indexed
    |> Seq.map youtubeItemsMapper
    |> curriedResultSender queryId offset

let private responseIsForbidden (ex: exn) =
    match ex.InnerException with
    | :? GoogleApiException as apiException -> apiException.Error.Code = 403
    | _ -> false

let private sendErrorToDeveloperAsync (inlineQuery, error) =
    bot.SendTextMessageAsync
        (applicationConfig.Telegram.DeveloperChatId
         |> ChatId,
         sprintf "Something went wrong.\nQuery: %s\nError: %s" (json (inlineQuery, Formatting.Indented))
             (json (error, Formatting.Indented)))
    |> Async.AwaitTask

let updateAsyncHandler token (update: Update) =
    fun (ctx: HttpContext) ->
        async {
            let list =
                [ "snippet" ] |> Repeatable |> service.Search.List

            let inlineQuery = update.InlineQuery

            let offset =
                match String.IsNullOrEmpty(inlineQuery.Offset) with
                | true -> pageLength
                | false -> inlineQuery.Offset |> int

            let keyFactory = sprintf "%s;%i" inlineQuery.Query

            match (offset - pageLength)
                  |> keyFactory
                  |> youtubeContinuationTokensCache.TryRetrieve with
            | Some res -> list.PageToken <- res
            | _ -> ()

            list.Q <- inlineQuery.Query
            list.Type <- [ "video" ] |> Repeatable
            list.MaxResults <- pageLength |> int64
            list.RelevanceLanguage <- inlineQuery.From.LanguageCode

            let! result =
                list.ExecuteAsync(token)
                |> Async.AwaitTask
                |> Async.Catch

            do! match result with
                | Choice1Of2 success -> sendInlineQueryResultAsync (inlineQuery.Id, offset, keyFactory, success)
                | Choice2Of2 error ->
                    sendErrorToDeveloperAsync (inlineQuery, error)
                    |> Async.Ignore

            return! OK String.Empty ctx
        }
