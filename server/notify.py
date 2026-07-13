# -*- coding: utf-8 -*-
"""管理用メール通知(design §3-8, §5)。

目的:①取得失敗 ②異常値の保留発生 を管理者に知らせ、
      「気づかず古い価格を配り続ける」事態を防ぐ。
smtp.enabled=false の間はコンソール出力にフォールバックする。
"""
from __future__ import annotations

import smtplib
import ssl
from email.mime.text import MIMEText


def send(cfg: dict, subject: str, body: str) -> None:
    smtp = cfg.get("smtp", {})
    if not smtp.get("enabled"):
        print(f"[notify:console] {subject}\n{body}")
        return
    msg = MIMEText(body, _charset="utf-8")
    msg["Subject"] = subject
    msg["From"] = smtp["from"]
    msg["To"] = ", ".join(smtp["to"])
    try:
        ctx = ssl.create_default_context()
        with smtplib.SMTP(smtp["host"], smtp["port"], timeout=20) as s:
            s.starttls(context=ctx)
            if smtp.get("user"):
                s.login(smtp["user"], smtp["password"])
            s.sendmail(smtp["from"], smtp["to"], msg.as_string())
        print(f"[notify:mail] sent: {subject}")
    except Exception as e:
        # 通知自体が失敗しても本処理は止めない(コンソールに残す)
        print(f"[notify:mail-FAILED] {e}\n  subject={subject}\n{body}")
