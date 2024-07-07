# SuperLobbyBot
Discord bot for displaying S&ASRT lobbies.

# Config
Environment variables: ```$DISCORD_TOKEN``` ```$STEAM_USER``` ```$STEAM_PASS``` ```$MESSAGE_WAIT``` ```$MESSAGE_COUNT```
web.cs -> ```HOST_ADDRESS = "<heroku-app-name>.herokuapp.com"```

# Build
```
docker build -t <docker-image-name> .
docker tag <docker-image-name> registry.heroku.com/<heroku-app-name>/web
```

# Deploy
```
heroku login
heroku container:login
heroku container:push web -a <heroku-app-name>
heroku container:release web -a <heroku-app-name>
```

# Web status
```
<heroku-app-name>.herokuapp.com
```