module TelegramAudioFinder.TelegramBot

open System
open System.Net
open System.Text
open System.Threading
open Google
open Google.Apis.Services
open Google.Apis.Util
open Google.Apis.YouTube.v3
open Google.Apis.YouTube.v3.Data
open Microsoft.FSharp.Collections
open Newtonsoft.Json
open SoundCloud.Api
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

let private curryingAnswerSender queryId offset items =
    bot.AnswerInlineQueryAsync
        (queryId,
         items,
         nextOffset = (offset |> string),
         cacheTime = cacheTimeout.Seconds,
         cancellationToken = cts.Token)
    |> Async.AwaitTask

[<Literal>]
let private thumbSideSize = 100

let private youtubeItemsMapper offset (index, item: SearchResult) =
    let snippet = item.Snippet

    let url =
        sprintf "https://youtube.com/watch?v=%s" item.Id.VideoId

    let res =
        InlineQueryResultArticle
            ((index + offset) |> string, snippet.Title |> WebUtility.HtmlDecode, InputTextMessageContent(url))

    res.Description <- snippet.Description
    res.ThumbUrl <- snippet.Thumbnails.Default__.Url
    res.ThumbHeight <- thumbSideSize
    res.ThumbWidth <- thumbSideSize
    res :> InlineQueryResultBase

let private optional s =
    if String.IsNullOrEmpty(s) then None else Some s

[<Literal>]
let private pageLength = 10

let private youtubeContinuationTokensCache = createInMemoryCache cacheTimeout

let private sendInlineQueryResultAsync (queryId, offset: int, youtubeItems: SearchResult list) =
    youtubeItems
    |> Seq.indexed
    |> Seq.map (youtubeItemsMapper offset)
    |> curryingAnswerSender queryId offset

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
    |> Async.Ignore


let private finder (inlineQuery: InlineQuery, token) =
    async {
        let list =
            [ "snippet" ] |> Repeatable |> service.Search.List

        let offset =
            match String.IsNullOrEmpty(inlineQuery.Offset) with
            | true -> 0
            | false -> inlineQuery.Offset |> int

        let keyFactory = sprintf "%s;%i" inlineQuery.Query

        match offset
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

        let nextPageOffset = offset + pageLength

        let curriedSender x =
            sendInlineQueryResultAsync (inlineQuery.Id, nextPageOffset, x)
            |> Async.Ignore

        do! match result with
            | Choice1Of2 success ->
                youtubeContinuationTokensCache.Set(keyFactory nextPageOffset, success.NextPageToken)
                success.Items |> Seq.toList |> curriedSender
            | Choice2Of2 error ->
                [ (sendErrorToDeveloperAsync (inlineQuery, error))
                  (curriedSender []) ]
                |> Async.Sequential
                |> Async.Ignore
    }

[<Literal>]
let private minimumKeywordLength = 3

let private helpDescription =
    $"/?h or /?help for help\nMinimum keyword length: {minimumKeywordLength}"

let private helpAsync (inlineQuery: InlineQuery, token) =
    let article =
        InlineQueryResultArticle("0", "Help", InputTextMessageContent(helpDescription))

    article.Description <- helpDescription

    bot.AnswerInlineQueryAsync(inlineQuery.Id, [ article ], 0, cancellationToken = token)
    |> Async.AwaitTask

type private Service =
    | YouTube
    | SoundCloud
    | All

type private Command =
    | Help
    | Search of Service
    | Wrong of string

let modifyQueryString (inlineQuery: InlineQuery, modified) =
    inlineQuery.Query <- modified
    inlineQuery

let private readSearchQuery command =
    match command with
    | "/?y"
    | "/?youtube" -> Search YouTube
    | "/?s"
    | "/?soundcloud" -> Search SoundCloud
    | "/?a"
    | "/?all" -> Search All
    | other -> Wrong other

let private readQuery (inlineQuery: InlineQuery) =
    let queryString = inlineQuery.Query
    let index = queryString.IndexOf(' ')

    if index = -1 then
        match queryString with
        | "/?h"
        | "/?help" -> Help, inlineQuery
        | command when command.StartsWith("/?") -> Wrong command, inlineQuery
        | _ -> Search All, inlineQuery
    else
        let (command, keywords) =
            queryString.Substring(0, index), queryString.Substring(index)

        match command with
        | "/?h"
        | "/?help" -> Help, inlineQuery
        | command when command.StartsWith("/?") ->
            match readSearchQuery command with
            | Wrong w -> Wrong w, inlineQuery
            | search -> search, modifyQueryString (inlineQuery, keywords)
        | _ -> Search All, inlineQuery

let private dispatchSearchAsync (search, query: InlineQuery, token) =
    match search with
    | YouTube
    | All -> finder (query, token)
    | SoundCloud -> Async.Sleep 0

let private sendWrongCommandResponseAsync (queryId: string, command) =
    let message = sprintf "Wrong command: %s." command

    let article =
        InlineQueryResultArticle("0", "Wrong command", InputTextMessageContent(message))

    article.Description <- message

    bot.AnswerInlineQueryAsync(queryId, [ article ])
    |> Async.AwaitTask

let private dispatchAsync (inlineQuery: InlineQuery, token) =
    match readQuery inlineQuery with
    | (Help, q) -> helpAsync (q, token)
    | (Search s, modifiedInlineQuery) -> dispatchSearchAsync (s, modifiedInlineQuery, token)
    | (Wrong w, q) -> sendWrongCommandResponseAsync (q.Id, w)

let updateAsyncHandler token (update: Update) =
    fun (ctx: HttpContext) ->
        async {
            let inlineQuery = update.InlineQuery

            do! if inlineQuery.Query.Length >= minimumKeywordLength then
                    dispatchAsync (inlineQuery, token)
                else
                    bot.AnswerInlineQueryAsync(inlineQuery.Id, [], 3600, cancellationToken = token)
                    |> Async.AwaitTask

            return! OK String.Empty ctx
        }
