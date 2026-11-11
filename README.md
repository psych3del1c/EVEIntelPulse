# EVE IntelPulse

Disclaimer

This program does not violate EVE Online EULA / Terms of Service as it only reads the clipboard and simulates standard keyboard input (Ctrl+C). Use at your own risk.


## Description

EVE IntelPulse is a Windows utility that lets you quickly open zKillboard via a hotkey.
Select something in chat (character, system, constellation, region, etc.) and press the hotkey.
Everything except names needs to be dragged into chat and selected there.

## Features

- Two operating modes: automatic (simulates Ctrl+C) and manual copy
- Supports characters, systems, constellations and regions


## How to Use

1. Download and run `EVEIntelPulse.exe`
2. Default hotkey: `Alt + Q`
3. Select text in the EVE Online client
4. Press the hotkey
5. zKillboard page will open in your default browser

**Manual copy mode** is recommended if you use clipboard managers or encounter issues with automatic copying.

## Troubleshooting

- The hotkey is triggered **after** the keys are released, so the game receives a clean `Ctrl+C` instead of `Alt+Ctrl+C`.
- If automatic copying still fails, check `%APPDATA%\EVE_IntelPulse\app.log` — it records the foreground window, the copy attempts and the clipboard timings.
- If the EVE client runs as administrator, run EVE IntelPulse as administrator too: Windows blocks simulated input from a lower-privileged process.
- Alternatively enable **Manual copy mode**: select the text, press `Ctrl+C` yourself, then press the hotkey.
- Only one instance can run at a time: a second copy would install the same global hotkey and open a second browser tab for every press, so it shows a message and exits.

## Installation

1. Go to the [Releases](https://github.com/psych3del1c/EVE-IntelPulse/releases)
2. Choose standalone or lightweight version (requires .NET 8 to work)
3. Download, unpack and run EVEIntelPulse.exe




Made by Arnold Olympus

===========================================================================




## Русская версия

## Описание

EVE IntelPulse — это утилита для Windows, которая позволяет быстро открывать zKillboard с помощью хоткея.   Выделяем в чате (персонажа, систему, консту, регион и т.д.) и нажимаем хоткей.  Всё кроме ников нужно перетащить в чат и выделить там

## Возможности

- Два режима работы: автоматический (симулирует Ctrl+C) и ручной
- Поддержка персонажей, систем, созвездий и регионов


## Как пользоваться

1. Скачайте и запустите `EVEIntelPulse.exe`
2. Горячая клавиша по умолчанию: `Alt + Q`
3. Выделите текст в клиенте EVE Online
4. Нажмите хоткей
5. Страница zKillboard откроется в браузере по умолчанию

Ручной режим рекомендуется, если вы используете менеджеры буфера обмена или испытываете проблемы с автоматическим копированием.

## Решение проблем

- Хоткей срабатывает **после отпускания** клавиш, поэтому в игру уходит чистый `Ctrl+C`, а не `Alt+Ctrl+C`.
- Если автоматическое копирование всё равно не работает, посмотрите `%APPDATA%\EVE_IntelPulse\app.log` — там пишется активное окно, попытки копирования и время ожидания буфера.
- Если клиент EVE запущен от администратора, запустите EVE IntelPulse тоже от администратора: Windows блокирует эмуляцию ввода из процесса с меньшими правами.
- Либо включите **ручной режим**: выделите текст, нажмите `Ctrl+C` сами, затем нажмите хоткей.
- Одновременно может работать только один экземпляр: вторая копия повесила бы тот же глобальный хоткей и открывала бы вторую вкладку на каждое нажатие, поэтому она показывает сообщение и закрывается.

## Установка

1. Перейдите в раздел [Releases](https://github.com/psych3del1c/EVE-IntelPulse/releases)
2. Выберите standalone или лёгкую версию (требуется .NET 8 для работы)
3. Скачайте, распакуйте и запустите `EVEIntelPulse.exe`




Made by Arnold Olympus

