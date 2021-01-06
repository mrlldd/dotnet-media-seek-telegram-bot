namespace TelegramAudioFinder

type TelegramConfig = {
    Token: string
    WebhookUrl: string
    MaxConnections: int
}

type Keys = {
    Youtube: string
}

type ApplicationConfig = {
    Telegram: TelegramConfig
    Keys: Keys
}