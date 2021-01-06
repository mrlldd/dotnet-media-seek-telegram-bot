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

type Services = {
    Youtube: YouTubeConfig
}

type ApplicationConfig = {
    Telegram: TelegramConfig
    Services: Services
}