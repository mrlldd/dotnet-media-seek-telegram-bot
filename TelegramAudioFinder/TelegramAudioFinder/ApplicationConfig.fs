namespace TelegramAudioFinder

type TelegramConfig = {
    Token: string
    WebhookUrl: string
    MaxConnections: int
    DeveloperChatId: int
}

type YouTubeConfig = {
    AppName: string
    Key: string
}

type ServicesConfig = {
    Youtube: YouTubeConfig
}

type ApplicationConfig = {
    Telegram: TelegramConfig
    Services: ServicesConfig
}