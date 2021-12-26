#!/bin/bash

echo Enter Facebook Page Access Token:
read token


### Get Started

curl -X POST -H "Content-Type: application/json" -d '{
  "get_started": {"payload": "--get-started--"}
}' "https://graph.facebook.com/v12.0/me/messenger_profile?access_token=$token"

echo ''


### Persistent Menu

# curl -X POST -H "Content-Type: application/json" -d '{
#     "persistent_menu": [
#         {
#             "locale": "default",
#             "composer_input_disabled": false,
#             "call_to_actions": [
#                 {
#                     "type": "postback",
#                     "title": "🏠 Αρχικό μενού",
#                     "payload": "--persistent-home--"
#                 },
# 		        {
#                     "type": "postback",
#                     "title": "ℹ️ Τι μπορώ να κάνω!",
#                     "payload": "--persistent-tutorial--"
#                 },
#                 {
#                     "type": "postback",
#                     "title": "👍 Αφήστε ένα σχόλιο!",
#                     "payload": "--persistent-feedback--"
#                 }
#             ]
#         }
#     ]
# }' "https://graph.facebook.com/v12.0/me/messenger_profile?access_token=$token"
# 
# echo ''


### Whitelisted Domains

curl -X POST -H "Content-Type: application/json" -d '{
  "whitelisted_domains": [
    "https://askphoenix.gr/",
    "https://www.askphoenix.gr/",
    "https://pwa.askphoenix.gr/",
    "https://teacher.askphoenix.gr/"
  ]
}' "https://graph.facebook.com/v12.0/me/messenger_profile?access_token=$token"

echo ''


### Greeting Text

# Καλωσορίσατε στον ψηφιακό βοηθό του {school_name}!
#echo Enter the greeting text:
#read greeting_text

curl -X POST -H "Content-Type: application/json" -d '{
  "greeting": [
    {
      "locale":"default",
      "text":"Καλωσορίσατε στον ψηφιακό βοηθό μας!"
    }
  ]
}' "https://graph.facebook.com/v12.0/me/messenger_profile?access_token=$token"

echo ''