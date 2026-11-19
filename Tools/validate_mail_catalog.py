"""Validate Cat Quest reward mail before a change is merged to main."""
import argparse
import datetime
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = "docs/mail/catalog.json"
REWARDS = ("fishCoins", "experience", "bossTickets")
DATE_FORMAT = "%Y-%m-%dT%H:%M:%SZ"
FIELDS = {"id", "title", "body", "publishedUtc", *REWARDS}


def check_catalog(raw):
    if len(raw) > 128 * 1024:
        raise ValueError("The catalogue must fit in 128 KiB.")
    data = json.loads(raw)
    if (not isinstance(data, dict) or set(data) != {"format", "letters"}
            or type(data["format"]) is not int or data["format"] != 1):
        raise ValueError("Expected format 1 with a letters array.")
    letters = data["letters"]
    if not isinstance(letters, list) or len(letters) > 100:
        raise ValueError("Expected at most 100 letters.")
    campaigns = {}
    for letter in letters:
        if not isinstance(letter, dict) or set(letter) != FIELDS:
            raise ValueError("A letter has missing or unexpected fields.")
        key = letter["id"]
        if not isinstance(key, str) or not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", key) or key in campaigns:
            raise ValueError(f"Invalid or duplicate mail ID {key!r}.")
        for name, maximum in (("title", 80), ("body", 3000)):
            value = letter[name]
            if not isinstance(value, str) or not value.strip() or len(value.encode("utf-16-le")) // 2 > maximum:
                raise ValueError(f"{key}: invalid {name}.")
        date = letter["publishedUtc"]
        if not isinstance(date, str) or not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z", date):
            raise ValueError(f"{key}: invalid UTC timestamp.")
        datetime.datetime.strptime(date, DATE_FORMAT)
        amounts = tuple(letter[name] for name in REWARDS)
        if any(type(value) is not int or not 0 <= value <= 100000 for value in amounts) or not any(amounts):
            raise ValueError(f"{key}: invalid attachments.")
        campaigns[key] = amounts
    return campaigns


def git(*args, **options):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, **options)


def validate_history(current):
    ref = "origin/main" if git("rev-parse", "--verify", "origin/main").returncode == 0 else "HEAD"
    history = git("log", "--format=%H", ref, "--", CATALOG, check=True)
    for commit in history.stdout.decode().splitlines():
        snapshot = git("show", f"{commit}:{CATALOG}")
        if snapshot.returncode != 0:
            continue
        for key, amounts in check_catalog(snapshot.stdout).items():
            if key in current and current[key] != amounts:
                raise ValueError(f"{key}: published attachment amounts cannot change.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", nargs="?", type=Path, default=ROOT / CATALOG)
    args = parser.parse_args()
    try:
        campaigns = check_catalog(args.path.read_bytes())
        if args.path.resolve() == (ROOT / CATALOG).resolve():
            validate_history(campaigns)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Invalid mail catalogue: {error}\n")
    print(f"Valid mail catalogue with {len(campaigns)} letter(s).")


if __name__ == "__main__":
    main()
