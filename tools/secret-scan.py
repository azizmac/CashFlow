#!/usr/bin/env python3
"""Проверка репозитория на чувствительные данные перед коммитом/пушем.

Ищет в отслеживаемых (или переданных) файлах: ключи и токены, приватные ключи, строки подключения с паролями,
реальные на вид ИНН/счета/карты/телефоны, e-mail. Синтетические реквизиты (счета вида 4070281010000000xxxx,
ИНН 77000000xx, телефоны +7 900 123-45-67 и т.п.) допускаются — см. ALLOW.

Запуск:  python tools/secret-scan.py            # все отслеживаемые файлы
         python tools/secret-scan.py --staged   # только проиндексированные (для pre-commit)
         python tools/secret-scan.py file1 file2
Код возврата 1, если что-то найдено.
"""
import re, subprocess, sys, os

SKIP_DIRS = ("docs/design/", ".git/", "bin/", "obj/", "node_modules/", "docs/vendor/")
SKIP_EXT = (".png", ".jpg", ".ico", ".svg", ".ttf", ".pdf", ".dll", ".exe", ".zip", ".xlsx", ".pfx")

# Синтетика, которую разрешаем в тестах и документации
ALLOW = [
    r"\b\d{5}810\d(?=\d*0{7})\d{11}\b",   # 40702810100000000123 — синтетические счета: семь и больше нулей подряд
    r"Password=design\b",                 # DesignTimeFactory для миграций
    r"(?i)password=\$\{", r"(?i)password=change-me", r"(?i)password=\(\?",  # плейсхолдеры compose/.env.example и сам сканер
    r"\b77000000\d{2}\b", r"\b7700000000\d{2}\b", r"\b123456789012\b",  # синтетические ИНН
    r"\b7707083893\b",                    # ИНН ПАО Сбербанк — публичный, нужен парсеру
    r"\b0000000000\d{2}\b",
    r"\+7 ?\(?900\)? ?123[- ]?45[- ]?67\b", r"\+79001234567\b", r"9001234567\b", r"8 \(900\) 123-45-67",
    r"\bdemo@cashflow\.local\b", r"[a-z]+@example\.com\b", r"you@example\.com",
    r"Password=\{pwd\}", r"Password=…", r"Password=\*\*\*", r"Password=cashflow\b",  # плейсхолдеры в коде/доках
    r"\b\d{20}\b(?=.*Migrations)",
]

PATTERNS = [
    ("приватный ключ", r"-----BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY-----"),
    ("ключ/токен в присваивании", r"(?i)\b(api[_-]?key|secret|token|client_secret|masterkey|master_key|access_key)\b\s*[=:]\s*[\"'][A-Za-z0-9+/=_\-]{16,}[\"']"),
    ("base64-ключ 32 байта", r"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{43}=(?![A-Za-z0-9+/=])"),
    ("пароль в строке подключения", r"(?i)Password=(?!\{)(?!…)(?!\*)[^;\"'\s]{6,}"),
    ("банковский счёт (20 цифр)", r"\b(?:40[0-9]|30[0-9]|42[0-9]|45[0-9]|47[0-9])\d{17}\b"),
    ("номер карты", r"\b(?:2200|2202|2204|4[0-9]{3}|5[1-5][0-9]{2}|220[0-9])[ -]?\d{4}[ -]?\d{4}[ -]?\d{4}\b"),
    ("ИНН (10/12 цифр)", r"(?i)ИНН[^0-9]{0,4}\b\d{10}(?:\d{2})?\b"),
    ("телефон", r"(?<!\d)(?:\+7|8)[ (]?\d{3}[ )]?[ -]?\d{3}[ -]?\d{2}[ -]?\d{2}(?!\d)"),
    ("e-mail", r"\b[A-Za-z0-9._%+-]+@(?:gmail|yandex|mail|bk|inbox|list|rambler|icloud|outlook)\.(?:com|ru)\b"),
]

def files_from_git(staged):
    cmd = ["git", "diff", "--cached", "--name-only", "--diff-filter=ACMR"] if staged else ["git", "ls-files"]
    out = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8").stdout
    return [f for f in out.splitlines() if f]

def main():
    args = sys.argv[1:]
    staged = "--staged" in args
    files = [a for a in args if not a.startswith("--")] or files_from_git(staged)
    allow = [re.compile(a) for a in ALLOW]
    pats = [(n, re.compile(p)) for n, p in PATTERNS]
    hits = 0
    for f in files:
        norm = f.replace("\\", "/")
        if any(norm.startswith(d) or ("/" + d) in norm for d in SKIP_DIRS) or norm.lower().endswith(SKIP_EXT):
            continue
        if not os.path.isfile(f) or norm.endswith("tools/secret-scan.py"):
            continue
        try:
            text = open(f, encoding="utf-8", errors="ignore").read()
        except OSError:
            continue
        for i, line in enumerate(text.splitlines(), 1):
            for name, p in pats:
                for m in p.finditer(line):
                    frag = m.group(0)
                    if any(a.search(frag) or a.search(line) for a in allow):
                        continue
                    print(f"{f}:{i}: {name}: {frag[:60]}")
                    hits += 1
    if hits:
        print(f"\nНайдено {hits} подозрительных мест. Замените реальные реквизиты вымышленными (см. .claude/skills/no-sensitive-data).")
        return 1
    print("Чувствительных данных не найдено.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
