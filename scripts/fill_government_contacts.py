#!/usr/bin/env python3
"""
Populate contact columns in a semicolon-separated government departments CSV.

Input columns expected:
  - Title
  - Address
  - Contact Person Particulars
  - Contact Email Address
  - Contact Phone Number

The script crawls each URL, extracts contact details, then backfills missing
values from matching department/ministry rows.
"""

from __future__ import annotations

import argparse
import csv
import difflib
import re
import subprocess
import unicodedata
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Iterable
from urllib.parse import urljoin, urlparse

import requests
import urllib3
from bs4 import BeautifulSoup
from requests.exceptions import RequestException, TooManyRedirects

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
)
REQUEST_TIMEOUT = 16
CURL_TIMEOUT = 28

EMAIL_RE = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")
PHONE_RE = re.compile(
    r"(?:(?:\+27|0)\s*(?:\(\s?0?\s?\)\s*)?(?:\d[\s\-()/]*){7,14}\d)"
)
YEAR_RE = re.compile(r"\b20\d{2}\b")

ROLE_KEYWORDS = (
    "minister",
    "deputy minister",
    "director-general",
    "director general",
    "commissioner",
    "private secretary",
    "assistant private secretary",
    "personal assistant",
    "secretary",
    "chief director",
)

ROLE_ONLY = {
    "minister",
    "deputy minister",
    "director-general",
    "director general",
    "commissioner",
    "private secretary",
    "assistant private secretary",
    "personal assistant",
    "secretary",
}

NEWS_WORDS = {
    "speech",
    "speeches",
    "meeting",
    "launch",
    "briefs",
    "briefing",
    "budget",
    "outcomes",
    "report",
    "reports",
    "announces",
    "anniversary",
    "conference",
    "address",
    "remarks",
    "statement",
    "media",
    "visit",
    "oversight",
    "questions",
    "session",
    "news",
    "vote",
    "summit",
}

CONTACT_HINTS = ("contact", "contact-us", "contactus", "directory", "enquiry", "enquiries")

BAD_EMAIL_DOMAINS = {"example.com", "example.org", "example.net"}
BAD_EMAIL_VALUES = {"demo@example.com"}
BAD_PHONE_DIGITS = {
    "0123456789",
    "1234567890",
    "0000000000",
    "1111111111",
    "2222222222",
    "3333333333",
    "4444444444",
    "5555555555",
    "6666666666",
    "7777777777",
    "8888888888",
    "9999999999",
}

TITLE_URL_OVERRIDES: dict[str, list[str]] = {
    # Authoritative directory pages that remain stable even when target sites block bots.
    "correctional services": [
        "https://www.gov.za/about-government/contact-directory/departments/correctional-services-department",
    ],
    "statistics south africa": [
        "https://www.gov.za/about-government/contact-directory/departments/statistics-south-africa-stats-sa",
    ],
    "sa revenue service": [
        "https://www.gov.za/about-government/contact-directory/departments/south-african-revenue-service-sars",
    ],
    "police": [
        "https://www.gov.za/about-government/contact-directory/ministers/police-ministry",
        "https://www.gov.za/about-government/contact-directory/departments/south-african-police-service-saps",
    ],
    "contact information": [
        "https://www.gov.za/about-government/contact-directory/national-government",
        "https://www.gcis.gov.za/",
    ],
}


@dataclass
class CrawlData:
    people: list[str] = field(default_factory=list)
    emails: list[str] = field(default_factory=list)
    phones: list[str] = field(default_factory=list)

    def merge(self, other: "CrawlData") -> None:
        self.people = unique(self.people + other.people)[:3]
        self.emails = unique(self.emails + other.emails)[:3]
        self.phones = unique(self.phones + other.phones)[:3]


@dataclass
class CrawlResult:
    row_index: int
    title: str
    address: str
    data: CrawlData
    ok: bool
    notes: list[str] = field(default_factory=list)


def unique(values: Iterable[str]) -> list[str]:
    out: list[str] = []
    seen: set[str] = set()
    for value in values:
        v = value.strip()
        if not v:
            continue
        if v in seen:
            continue
        seen.add(v)
        out.append(v)
    return out


def normalize_space(value: str) -> str:
    return " ".join(value.split())


def ascii_fold(value: str) -> str:
    return unicodedata.normalize("NFKD", value).encode("ascii", "ignore").decode("ascii")


def canonical_title(title: str) -> str:
    t = ascii_fold(title).lower()
    t = re.sub(r"\([^)]*\)", " ", t)
    t = t.replace("&", " and ")
    t = re.sub(r"[^a-z0-9 ]+", " ", t)
    t = normalize_space(t)

    if "electricity and energy" in t or "mineral and petroleum resources" in t:
        return "energy minerals"
    if t.startswith("higher education"):
        return "higher education"
    if "justice and correctional services" in t:
        return "justice and constitutional development"
    if "correctional services" in t:
        return "justice and constitutional development"
    if "sa police service" in t:
        return "police"
    if "presidency" in t:
        return "presidency"
    if "trade industry" in t and "competition" in t:
        return "trade industry and competition"
    return t


def clean_email(value: str) -> str | None:
    email = value.strip().strip(".,;:()[]<>")
    email = email.replace("mailto:", "")
    if not email:
        return None
    email = email.split("?")[0].strip()
    if "@" not in email:
        return None
    low = email.lower()
    domain = low.split("@", 1)[1]
    if low in BAD_EMAIL_VALUES:
        return None
    if domain in BAD_EMAIL_DOMAINS:
        return None
    return email


def format_sa_phone(digits: str) -> str:
    if digits.startswith("27") and len(digits) == 11:
        return f"+27 {digits[2:4]} {digits[4:7]} {digits[7:11]}"
    if digits.startswith("0") and len(digits) == 10:
        return f"{digits[:3]} {digits[3:6]} {digits[6:10]}"
    return digits


def clean_phone(value: str) -> str | None:
    text = normalize_space(value)
    if not text:
        return None
    if YEAR_RE.search(text) and not re.search(r"(\+27|0)\D*\d{8,}", text):
        return None

    digits = re.sub(r"\D", "", text)
    if digits.startswith("0027"):
        digits = digits[2:]

    if digits in BAD_PHONE_DIGITS:
        return None
    if len(set(digits)) == 1:
        return None

    # South African numbers: local 0xxxxxxxxx (10) or +27xxxxxxxxx (11 digits w/o '+')
    valid = False
    if digits.startswith("0") and len(digits) == 10:
        valid = True
    if digits.startswith("27") and len(digits) == 11:
        valid = True

    if not valid:
        return None

    return format_sa_phone(digits)


def looks_like_news(value: str) -> bool:
    low = value.lower()
    return any(word in low for word in NEWS_WORDS)


def is_role_only(value: str) -> bool:
    low = value.lower().strip(" :;,.")
    return low in ROLE_ONLY


def looks_name_like(value: str) -> bool:
    s = value.strip()
    if not s or len(s) > 90:
        return False
    if "@" in s or "http" in s.lower():
        return False
    if looks_like_news(s):
        return False
    if sum(ch.isdigit() for ch in s) > 2:
        return False

    words = [w.strip(" ,:;()[]") for w in s.split()]
    words = [w for w in words if w]
    if len(words) < 2:
        return False

    starts_upper = sum(1 for w in words if w and w[0].isalpha() and w[0].isupper())
    has_title = bool(re.search(r"\b(Mr|Ms|Mrs|Dr|Prof|Adv)\b", s))
    return starts_upper >= 2 or has_title


def extract_lines(soup: BeautifulSoup) -> list[str]:
    for tag in soup(["script", "style", "noscript"]):
        tag.decompose()
    lines: list[str] = []
    seen: set[str] = set()
    for raw in soup.get_text("\n").splitlines():
        line = normalize_space(raw)
        if not line:
            continue
        if line in seen:
            continue
        seen.add(line)
        lines.append(line)
    return lines


def extract_emails(soup: BeautifulSoup, lines: list[str]) -> list[str]:
    emails: list[str] = []

    for a in soup.select("a[href^='mailto:']"):
        cleaned = clean_email(a.get("href", ""))
        if cleaned:
            emails.append(cleaned)

    for line in lines:
        for found in EMAIL_RE.findall(line):
            cleaned = clean_email(found)
            if cleaned:
                emails.append(cleaned)

    return unique(emails)[:3]


def extract_phones(soup: BeautifulSoup, lines: list[str]) -> list[str]:
    phones: list[str] = []

    for a in soup.select("a[href^='tel:']"):
        cleaned = clean_phone(a.get("href", "").replace("tel:", ""))
        if cleaned:
            phones.append(cleaned)

    for line in lines:
        for candidate in PHONE_RE.findall(line):
            cleaned = clean_phone(candidate)
            if cleaned:
                phones.append(cleaned)

    # Dedupe by digits so format variants do not repeat.
    out: list[str] = []
    seen_digits: set[str] = set()
    for p in phones:
        digits = re.sub(r"\D", "", p)
        if digits in seen_digits:
            continue
        seen_digits.add(digits)
        out.append(p)
    return out[:3]


def extract_people(lines: list[str]) -> list[str]:
    candidates: list[str] = []

    for idx, line in enumerate(lines):
        low = line.lower()
        if not any(k in low for k in ROLE_KEYWORDS):
            continue
        if looks_like_news(line):
            continue
        if len(line) > 120:
            continue

        entry = line
        if is_role_only(line) or line.rstrip().endswith(":"):
            for probe in lines[idx + 1 : idx + 4]:
                if looks_name_like(probe):
                    entry = f"{line.rstrip(' :')} : {probe}"
                    break

        if is_role_only(entry):
            continue
        if looks_like_news(entry):
            continue
        if len(entry) > 120:
            continue
        candidates.append(entry)

    # Fallback: pick explicit titled names if role extraction produced nothing.
    if not candidates:
        for line in lines:
            if not re.search(r"\b(Mr|Ms|Mrs|Dr|Prof|Adv)\b", line):
                continue
            if looks_name_like(line):
                candidates.append(line)
            if len(candidates) >= 3:
                break

    return unique(candidates)[:3]


def parse_page(url: str, html: str) -> tuple[CrawlData, list[str]]:
    soup = BeautifulSoup(html, "html.parser")
    lines = extract_lines(soup)
    data = CrawlData(
        people=extract_people(lines),
        emails=extract_emails(soup, lines),
        phones=extract_phones(soup, lines),
    )
    links = contact_links(url, soup)
    return data, links


def contact_links(url: str, soup: BeautifulSoup) -> list[str]:
    base = urlparse(url)
    root_domain = base.netloc.lower().lstrip("www.")
    links: list[str] = []

    for a in soup.find_all("a", href=True):
        href = a.get("href", "").strip()
        if not href:
            continue
        text = f"{a.get_text(' ', strip=True)} {href}".lower()
        if not any(h in text for h in CONTACT_HINTS):
            continue
        full = urljoin(url, href).split("#", 1)[0]
        parsed = urlparse(full)
        if parsed.scheme not in {"http", "https"}:
            continue
        domain = parsed.netloc.lower().lstrip("www.")
        if domain != root_domain and not domain.endswith(root_domain):
            continue
        links.append(full)

    return unique(links)[:3]


class Fetcher:
    def __init__(self) -> None:
        self.headers = {"User-Agent": USER_AGENT}
        self.cache: dict[str, tuple[str | None, str | None, str | None]] = {}
        self.minister_links: list[tuple[str, str]] | None = None

    def fetch(self, url: str) -> tuple[str | None, str | None, str | None]:
        if url in self.cache:
            return self.cache[url]

        html, final_url, err = self._fetch_requests(url)
        if html:
            self.cache[url] = (html, final_url or url, None)
            return self.cache[url]

        curl_html, curl_err = self._fetch_curl(url)
        if curl_html:
            self.cache[url] = (curl_html, url, None)
            return self.cache[url]

        self.cache[url] = (None, None, err or curl_err or "Unknown fetch error")
        return self.cache[url]

    def _fetch_requests(self, url: str) -> tuple[str | None, str | None, str | None]:
        try:
            resp = requests.get(
                url,
                headers=self.headers,
                timeout=REQUEST_TIMEOUT,
                verify=False,
                allow_redirects=True,
            )
            if resp.status_code >= 400:
                return None, resp.url, f"HTTP {resp.status_code}"
            text = resp.text or ""
            if len(text) < 120:
                return None, resp.url, "Response too short"
            return text, resp.url, None
        except TooManyRedirects as exc:
            return None, None, f"TooManyRedirects: {exc}"
        except RequestException as exc:
            return None, None, str(exc)

    def _fetch_curl(self, url: str) -> tuple[str | None, str | None]:
        cmd = [
            "curl",
            "-Lk",
            "--max-redirs",
            "15",
            "--max-time",
            str(CURL_TIMEOUT),
            url,
        ]
        try:
            proc = subprocess.run(
                cmd,
                text=True,
                capture_output=True,
                check=False,
                encoding="utf-8",
                errors="replace",
            )
        except OSError as exc:
            return None, f"curl unavailable: {exc}"

        if proc.returncode != 0:
            msg = proc.stderr.strip() or proc.stdout.strip() or f"curl rc={proc.returncode}"
            return None, msg

        body = proc.stdout
        if len(body) < 120:
            return None, "curl response too short"
        return body, None

    def resolve_minister_url(self, title: str) -> str | None:
        if self.minister_links is None:
            seed_url = "https://www.gov.za/about-government/contact-directory/ministers"
            html, _, err = self.fetch(seed_url)
            if not html:
                return None
            soup = BeautifulSoup(html, "html.parser")
            links: list[tuple[str, str]] = []
            for a in soup.find_all("a", href=True):
                href = urljoin(seed_url, a["href"])
                if "/about-government/contact-directory/ministers/" not in href:
                    continue
                text = normalize_space(a.get_text(" ", strip=True))
                if not text:
                    continue
                links.append((canonical_title(text), href))
            self.minister_links = unique_tuple(links)

        target = canonical_title(title)
        best_url: str | None = None
        best_score = 0.0
        for label, href in self.minister_links:
            score = difflib.SequenceMatcher(None, target, label).ratio()
            if target and target in label:
                score += 0.2
            if score > best_score:
                best_score = score
                best_url = href
        if best_score >= 0.48:
            return best_url
        return None


def unique_tuple(values: Iterable[tuple[str, str]]) -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    seen: set[tuple[str, str]] = set()
    for item in values:
        if item in seen:
            continue
        seen.add(item)
        out.append(item)
    return out


def crawl_row(fetcher: Fetcher, row_index: int, title: str, address: str) -> CrawlResult:
    data = CrawlData()
    notes: list[str] = []
    checked: list[str] = []

    def try_url(url: str) -> tuple[bool, list[str]]:
        html, final_url, err = fetcher.fetch(url)
        if not html:
            notes.append(f"{url} -> {err}")
            return False, []

        parsed, links = parse_page(final_url or url, html)
        data.merge(parsed)
        checked_links = []
        # Follow contact-ish pages when needed.
        need_more = (not data.emails) or (not data.phones) or (not data.people)
        if need_more:
            for link in links:
                if link in checked or link == url:
                    continue
                checked.append(link)
                html2, final2, err2 = fetcher.fetch(link)
                if not html2:
                    notes.append(f"{link} -> {err2}")
                    continue
                parsed2, _ = parse_page(final2 or link, html2)
                data.merge(parsed2)
                checked_links.append(link)
                if data.emails and data.phones and data.people:
                    break
        return True, checked_links

    checked.append(address)
    ok, _ = try_url(address)

    # Fallback for broken gov.za minister links.
    if (not ok or (not data.emails and not data.phones and not data.people)) and "gov.za" in address:
        if "/contact-directory/ministers/" in address:
            resolved = fetcher.resolve_minister_url(title)
            if resolved and resolved not in checked:
                checked.append(resolved)
                ok2, _ = try_url(resolved)
                if ok2:
                    notes.append(f"resolved_minister_url={resolved}")
                    ok = True

    # Targeted department fallbacks for URLs that are offline, blocked, or incomplete.
    override_key = canonical_title(title)
    for fallback_url in TITLE_URL_OVERRIDES.get(override_key, []):
        if fallback_url in checked:
            continue
        if data.people and data.emails and data.phones:
            break
        checked.append(fallback_url)
        ok3, _ = try_url(fallback_url)
        if ok3:
            notes.append(f"title_override_url={fallback_url}")
            ok = True

    return CrawlResult(
        row_index=row_index,
        title=title,
        address=address,
        data=data,
        ok=ok and bool(data.emails or data.phones or data.people),
        notes=notes,
    )


def backfill_by_title(results: list[CrawlResult]) -> int:
    pools: dict[str, CrawlData] = defaultdict(CrawlData)
    for result in results:
        key = canonical_title(result.title)
        pools[key].merge(result.data)

    changed = 0
    for result in results:
        key = canonical_title(result.title)
        pool = pools[key]
        before = (len(result.data.people), len(result.data.emails), len(result.data.phones))
        if not result.data.people:
            result.data.people = pool.people[:3]
        if not result.data.emails:
            result.data.emails = pool.emails[:3]
        if not result.data.phones:
            result.data.phones = pool.phones[:3]
        after = (len(result.data.people), len(result.data.emails), len(result.data.phones))
        if after != before:
            changed += 1
    return changed


def write_output(
    input_rows: list[dict[str, str]],
    results: list[CrawlResult],
    output_path: Path,
) -> None:
    by_index = {r.row_index: r for r in results}
    fieldnames = [
        "Title",
        "Address",
        "Contact Person Particulars",
        "Contact Email Address",
        "Contact Phone Number",
    ]

    with output_path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, delimiter=";")
        writer.writeheader()
        for row_idx, src in enumerate(input_rows, start=2):
            result = by_index[row_idx]
            out = dict(src)
            out["Contact Person Particulars"] = " | ".join(result.data.people)
            out["Contact Email Address"] = " | ".join(result.data.emails)
            out["Contact Phone Number"] = " | ".join(result.data.phones)
            writer.writerow(out)


def write_log(results: list[CrawlResult], log_path: Path, input_path: Path, output_path: Path, backfills: int) -> None:
    failures = [r for r in results if not (r.data.people or r.data.emails or r.data.phones)]
    missing_people = sum(1 for r in results if not r.data.people)
    missing_emails = sum(1 for r in results if not r.data.emails)
    missing_phones = sum(1 for r in results if not r.data.phones)

    with log_path.open("w", encoding="utf-8") as handle:
        handle.write(f"Input: {input_path}\n")
        handle.write(f"Output: {output_path}\n")
        handle.write(f"Rows: {len(results)}\n")
        handle.write(f"Rows backfilled by title match: {backfills}\n")
        handle.write(f"Rows with no extracted data: {len(failures)}\n")
        handle.write(f"Rows missing Contact Person Particulars: {missing_people}\n")
        handle.write(f"Rows missing Contact Email Address: {missing_emails}\n")
        handle.write(f"Rows missing Contact Phone Number: {missing_phones}\n")
        handle.write("\n")

        if failures:
            handle.write("Rows still empty:\n")
            for r in failures:
                handle.write(f"Row {r.row_index} | {r.title} | {r.address}\n")
                for note in r.notes:
                    handle.write(f"  - {note}\n")
            handle.write("\n")

        handle.write("Per-row crawl notes (only rows with notes):\n")
        for r in results:
            if not r.notes:
                continue
            handle.write(f"Row {r.row_index} | {r.title}\n")
            for note in r.notes:
                handle.write(f"  - {note}\n")


def read_input(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle, delimiter=";"))


def validate_columns(rows: list[dict[str, str]]) -> None:
    if not rows:
        raise ValueError("Input CSV has no data rows.")
    required = {
        "Title",
        "Address",
        "Contact Person Particulars",
        "Contact Email Address",
        "Contact Phone Number",
    }
    actual = set(rows[0].keys())
    missing = sorted(required - actual)
    if missing:
        raise ValueError(f"Input CSV missing required columns: {missing}")


def run(input_path: Path, exports_dir: Path) -> tuple[Path, Path]:
    rows = read_input(input_path)
    validate_columns(rows)

    fetcher = Fetcher()
    results: list[CrawlResult] = []
    for idx, row in enumerate(rows, start=2):
        title = (row.get("Title") or "").strip()
        address = (row.get("Address") or "").strip()
        if not address:
            results.append(
                CrawlResult(
                    row_index=idx,
                    title=title,
                    address=address,
                    data=CrawlData(),
                    ok=False,
                    notes=["missing Address URL"],
                )
            )
            continue
        results.append(crawl_row(fetcher, idx, title, address))

    backfills = backfill_by_title(results)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_path = exports_dir / f"{input_path.stem}_completed_{timestamp}.csv"
    log_path = exports_dir / f"{input_path.stem}_completed_{timestamp}_crawl_log.txt"

    write_output(rows, results, output_path)
    write_log(results, log_path, input_path, output_path, backfills)
    return output_path, log_path


def main() -> int:
    parser = argparse.ArgumentParser(description="Crawl department URLs and fill contact columns in CSV.")
    parser.add_argument(
        "--input",
        default=r"E:\ETDP\ETDP\Requests\3-List of Government Departments.csv",
        help="Path to source CSV file (semicolon-delimited).",
    )
    parser.add_argument(
        "--exports",
        default=r"E:\ETDP\ETDP\Exports",
        help="Directory for output CSV and crawl log.",
    )
    args = parser.parse_args()

    input_path = Path(args.input)
    exports_dir = Path(args.exports)
    exports_dir.mkdir(parents=True, exist_ok=True)

    output_path, log_path = run(input_path, exports_dir)
    print(f"Completed CSV: {output_path}")
    print(f"Crawl log: {log_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
