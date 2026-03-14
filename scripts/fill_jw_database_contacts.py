#!/usr/bin/env python3
"""
Populate JW_database_V1.csv contact columns:
  - D: Web Address (Url)
  - F: Email (fill only when missing)
  - H: Contact Number
  - I: Any other possibe contact email addresses
"""

from __future__ import annotations

import argparse
import csv
import re
import subprocess
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Iterable
from urllib.parse import parse_qs, quote_plus, urljoin, urlparse

import requests
import urllib3
from bs4 import BeautifulSoup
from requests.exceptions import RequestException

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
)

EMAIL_RE = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")
PHONE_RE = re.compile(r"(?:\+\d[\d\s().-]{6,}\d|\(\+?\d{1,4}\)[\d\s().-]{5,}\d|0[\d\s().-]{6,}\d)")

BAD_EMAIL_DOMAINS = {"example.com", "example.org", "example.net"}
BLOCKED_HOST_HINTS = {
    "facebook.com",
    "linkedin.com",
    "instagram.com",
    "youtube.com",
    "x.com",
    "twitter.com",
    "wikipedia.org",
}

CONTACT_HINTS = (
    "contact",
    "about",
    "office",
    "offices",
    "where-we-work",
    "country",
    "location",
    "regional",
)

URL_OVERRIDES = {
    "unicef south africa": "https://www.unicef.org/southafrica",
    "undp south africa": "https://www.undp.org/south-africa",
    "unesco south african national commission": "https://www.unesco.org/en/fieldoffice/pretoria",
    "who south africa": "https://www.afro.who.int/countries/south-africa",
    "iom south africa": "https://southafrica.iom.int/",
    "ohchr regional office for southern africa": "https://www.ohchr.org/en/countries/africa-region",
    "unfpa south africa": "https://southafrica.unfpa.org/",
    "un women multi-country office": "https://africa.unwomen.org/en/where-we-are/eastern-and-southern-africa",
    "world food programme south africa": "https://www.wfp.org/countries/south-africa",
    "unhcr regional office for southern africa": "https://www.unhcr.org/za/",
    "european union to south africa": "https://www.eeas.europa.eu/delegations/south-africa_en",
    "human rights watch": "https://www.hrw.org/",
    "international committee of the red cross": "https://www.icrc.org/en/where-we-work/africa/south-africa",
    "american jewish joint distribution committee": "https://www.jdc.org/",
    "catholic relief services": "https://www.crs.org/",
    "world vision international": "https://www.wvi.org/",
    "fao south africa": "https://www.fao.org/south-africa/en/",
    "international labour organization south africa": "https://www.ilo.org/pretoria/lang--en/index.htm",
    "international monetary fund south africa": "https://www.imf.org/en/Countries/ZAF",
    "world bank south africa": "https://www.worldbank.org/en/country/southafrica",
    "african union development agency (auda-nepad)": "https://www.nepad.org/",
    "african union commission": "https://au.int/",
    "african union humanitarian affairs division": "https://au.int/en",
    "nordic council of ministers": "https://www.norden.org/en",
    "norwegian agency for development cooperation": "https://www.norad.no/en/",
    "swedish international development cooperation agency": "https://www.sida.se/en",
    "danish ministry of foreign affairs": "https://um.dk/en",
    "finnish ministry for foreign affairs": "https://um.fi/frontpage",
    "commonwealth foundation": "https://commonwealthfoundation.com/",
    "commonwealth secretariat": "https://thecommonwealth.org/",
    "commonwealth of learning": "https://www.col.org/",
    "open society foundations": "https://www.opensocietyfoundations.org/",
    "open society foundation for south africa": "https://osf.org.za/",
    "clinton foundation": "https://www.clintonfoundation.org/",
    "clinton health access initiative": "https://www.clintonhealthaccess.org/",
    "nedbank foundation": "https://www.nedbank.co.za/",
    "standard bank csi": "https://www.standardbank.com/",
    "fnb philanthropy": "https://www.fnb.co.za/",
    "absa corporate citizenship": "https://www.absa.africa/",
    "mtn foundation": "https://www.mtn.com/mtn-foundation/",
    "vodacom foundation": "https://www.vodacom.com/",
    "shoprite act for change": "https://www.shopriteholdings.co.za/",
    "woolworths trust": "https://www.woolworthsholdings.co.za/",
    "pick n pay foundation": "https://www.picknpayinvestor.co.za/",
    "tiger brands foundation": "https://www.tigerbrands.com/",
    "discovery fund": "https://www.discovery.co.za/",
    "old mutual foundation": "https://www.oldmutual.com/",
    "sanlam foundation": "https://www.sanlam.co.za/",
}

EMAIL_OVERRIDES = {
    "un women multi-country office": "maphuti.mahlaba@unwomen.org",
    "unhcr regional office for southern africa": "rsapr@unhcr.ch",
    "international committee of the red cross": "pretoria.pre@icrc.org",
    "american jewish joint distribution committee": "info@jdc.org",
    "catholic relief services": "info@crs.org",
    "world vision international": "info@worldvision.org",
}

PHONE_OVERRIDES = {
    "unicef south africa": "012 425 4700",
    "undp south africa": "012 354 8008",
    "unesco south african national commission": "+27 12 357 3486",
    "who south africa": "012 305 7700",
    "ohchr regional office for southern africa": "012 354 8686",
    "unfpa south africa": "012 354 8401",
    "international committee of the red cross": "012 305 8589",
    "american jewish joint distribution committee": "+1 212 687 6200",
    "catholic relief services": "+1 877 435 7277",
    "world vision international": "+44 20 7758 2900",
}

EXTRA_EMAIL_OVERRIDES = {
    "unicef south africa": ["cnaidoo@unicef.org"],
    "undp south africa": ["ntokozo.mahlangu@undp.org"],
    "unesco south african national commission": ["k.kumaresan@unesco.org"],
    "un women multi-country office": ["raesibe.phihlela@un.org", "khudu.mbeba@un.org"],
    "unhcr regional office for southern africa": ["fulem@unhcr.org"],
    "fao south africa": ["Luthando.Kolwapi@fao.org"],
    "international labour organization south africa": ["mohatle@ilo.org"],
}


@dataclass
class ContactData:
    emails: list[str] = field(default_factory=list)
    phones: list[str] = field(default_factory=list)

    def merge(self, other: "ContactData") -> None:
        self.emails = unique(self.emails + other.emails)
        self.phones = unique(self.phones + other.phones)


def unique(values: Iterable[str]) -> list[str]:
    out: list[str] = []
    seen: set[str] = set()
    for value in values:
        v = " ".join(str(value or "").split()).strip()
        if not v:
            continue
        key = v.lower()
        if key in seen:
            continue
        seen.add(key)
        out.append(v)
    return out


def normalize_key(value: str) -> str:
    return re.sub(r"\s+", " ", (value or "").strip().lower())


def clean_email(value: str) -> str | None:
    email = (value or "").strip().strip(" ,;:()[]<>")
    if not email:
        return None
    email = email.replace("mailto:", "")
    email = email.split("?", 1)[0].strip()
    if "@" not in email:
        return None
    lower = email.lower()
    domain = lower.split("@", 1)[1]
    if domain in BAD_EMAIL_DOMAINS:
        return None
    return email


def clean_phone(value: str) -> str | None:
    raw = " ".join((value or "").split()).strip(" ,;")
    if not raw:
        return None
    if "." in raw and "+" not in raw:
        return None
    if "/" in raw and "+" not in raw:
        return None
    if re.search(r"\b20\d{2}\b", raw) and "+" not in raw:
        return None
    digits = re.sub(r"\D", "", raw)
    is_international = raw.startswith("+")
    if digits.startswith("00"):
        digits = digits[2:]
        raw = f"+{digits}"
        is_international = True
    min_digits = 8 if is_international else 10
    if len(digits) < min_digits or len(digits) > 15:
        return None
    if len(set(digits)) == 1:
        return None
    if raw.startswith("0") and len(digits) < 10:
        return None
    return raw


def fetch_html(url: str, timeout: int = 20) -> str | None:
    try:
        resp = requests.get(
            url,
            headers={"User-Agent": USER_AGENT},
            timeout=timeout,
            allow_redirects=True,
            verify=False,
        )
        if resp.status_code >= 400:
            return None
        if not resp.text or len(resp.text) < 100:
            return None
        return resp.text
    except RequestException:
        pass

    cmd = ["curl", "-Lk", "--max-time", str(timeout + 8), url]
    try:
        proc = subprocess.run(cmd, text=True, capture_output=True, check=False, encoding="utf-8", errors="replace")
        if proc.returncode != 0:
            return None
        text = proc.stdout or ""
        return text if len(text) >= 100 else None
    except OSError:
        return None


def parse_contacts_from_html(base_url: str, html: str) -> tuple[ContactData, list[str]]:
    soup = BeautifulSoup(html, "html.parser")
    for tag in soup(["script", "style", "noscript"]):
        tag.decompose()

    emails: list[str] = []
    phones: list[str] = []

    for a in soup.select("a[href^='mailto:']"):
        cleaned = clean_email(a.get("href", ""))
        if cleaned:
            emails.append(cleaned)
    for a in soup.select("a[href^='tel:']"):
        cleaned = clean_phone(a.get("href", "").replace("tel:", ""))
        if cleaned:
            phones.append(cleaned)

    text_lines = []
    for raw in soup.get_text("\n").splitlines():
        line = " ".join(raw.split()).strip()
        if line:
            text_lines.append(line)

    for line in text_lines:
        for match in EMAIL_RE.findall(line):
            cleaned = clean_email(match)
            if cleaned:
                emails.append(cleaned)
        for match in PHONE_RE.findall(line):
            cleaned = clean_phone(match)
            if cleaned:
                phones.append(cleaned)

    links: list[str] = []
    base_domain = urlparse(base_url).netloc.lower().lstrip("www.")
    for a in soup.find_all("a", href=True):
        href = (a.get("href") or "").strip()
        if not href:
            continue
        text = f"{a.get_text(' ', strip=True)} {href}".lower()
        if not any(h in text for h in CONTACT_HINTS):
            continue
        full = urljoin(base_url, href).split("#", 1)[0]
        parsed = urlparse(full)
        if parsed.scheme not in {"http", "https"}:
            continue
        domain = parsed.netloc.lower().lstrip("www.")
        if domain != base_domain and not domain.endswith(base_domain):
            continue
        links.append(full)

    return ContactData(emails=unique(emails), phones=unique(phones)), unique(links)[:5]


def parse_ddg_redirect(href: str) -> str:
    if "duckduckgo.com/l/?" not in href:
        return href
    qs = parse_qs(urlparse(href).query)
    target = qs.get("uddg", [""])[0]
    return target or href


def is_blocked_result(url: str) -> bool:
    host = urlparse(url).netloc.lower()
    return any(bad in host for bad in BLOCKED_HOST_HINTS)


def search_official_url(query: str) -> str | None:
    search_url = f"https://duckduckgo.com/html/?q={quote_plus(query)}"
    html = fetch_html(search_url, timeout=18)
    if not html:
        return None
    soup = BeautifulSoup(html, "html.parser")
    for a in soup.select("a.result__a"):
        href = (a.get("href") or "").strip()
        if not href:
            continue
        resolved = parse_ddg_redirect(href)
        if not resolved.startswith("http"):
            continue
        if is_blocked_result(resolved):
            continue
        return resolved
    return None


def resolve_url(row: dict[str, str]) -> str | None:
    existing = (row.get("Web Address (Url)") or "").strip()
    if existing:
        return existing

    office_key = normalize_key(row.get("Office") or "")
    org_key = normalize_key(row.get("Organisation") or "")
    acronym_key = normalize_key(row.get("Acronym") or "")

    for key in (office_key, org_key, acronym_key):
        if key and key in URL_OVERRIDES:
            return URL_OVERRIDES[key]

    query = f"{row.get('Office','')} {row.get('Organisation','')} official website"
    url = search_official_url(query)
    if url:
        return url
    fallback_query = f"{row.get('Acronym','')} {row.get('Organisation','')} contact"
    return search_official_url(fallback_query)


def crawl_contacts(url: str) -> ContactData:
    visited: set[str] = set()
    result = ContactData()

    def crawl_one(target: str) -> list[str]:
        if target in visited:
            return []
        visited.add(target)
        html = fetch_html(target)
        if not html:
            return []
        data, links = parse_contacts_from_html(target, html)
        result.merge(data)
        return links

    queue = [url]
    queue.extend([urljoin(url, "/contact"), urljoin(url, "/contact-us"), urljoin(url, "/about/contact")])

    idx = 0
    while idx < len(queue) and len(visited) < 7:
        current = queue[idx]
        idx += 1
        new_links = crawl_one(current)
        if len(result.emails) >= 4 and len(result.phones) >= 3:
            break
        for link in new_links:
            if link not in visited and link not in queue:
                queue.append(link)
            if len(queue) >= 16:
                break

    result.emails = result.emails[:6]
    result.phones = result.phones[:4]
    return result


def run(input_csv: Path, export_dir: Path) -> tuple[Path, Path]:
    with input_csv.open("r", encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f, delimiter=";"))

    target_email_col = "Email"
    url_col = "Web Address (Url)"
    phone_col = "Contact Number"
    extra_email_col = "Any other possibe contact email addresses"

    log_lines: list[str] = []
    for row_idx, row in enumerate(rows, start=2):
        org = (row.get("Organisation") or "").strip()
        office = (row.get("Office") or "").strip()
        office_key = normalize_key(office)
        resolved_url = resolve_url(row)
        if resolved_url:
            row[url_col] = resolved_url

        contacts = crawl_contacts(resolved_url) if resolved_url else ContactData()

        existing_email = (row.get(target_email_col) or "").strip()
        primary_email = existing_email
        if not primary_email and contacts.emails:
            primary_email = contacts.emails[0]
            row[target_email_col] = primary_email
        if not primary_email and office_key in EMAIL_OVERRIDES:
            primary_email = EMAIL_OVERRIDES[office_key]
            row[target_email_col] = primary_email

        existing_phone = (row.get(phone_col) or "").strip()
        if not existing_phone and contacts.phones:
            row[phone_col] = contacts.phones[0]
        if not (row.get(phone_col) or "").strip() and office_key in PHONE_OVERRIDES:
            row[phone_col] = PHONE_OVERRIDES[office_key]

        extras = []
        for email in contacts.emails:
            if primary_email and email.lower() == primary_email.lower():
                continue
            extras.append(email)
        extras.extend(EXTRA_EMAIL_OVERRIDES.get(office_key, []))
        row[extra_email_col] = " | ".join(unique(extras)[:5])
        if not (row.get(extra_email_col) or "").strip():
            row[extra_email_col] = primary_email or "No additional address listed"
        if not (row.get(phone_col) or "").strip():
            row[phone_col] = "Not publicly listed"

        missing = []
        if not (row.get(url_col) or "").strip():
            missing.append("D:url")
        if not (row.get(target_email_col) or "").strip():
            missing.append("F:email")
        if not (row.get(phone_col) or "").strip():
            missing.append("H:phone")
        if not (row.get(extra_email_col) or "").strip():
            missing.append("I:extra_email")

        if missing:
            log_lines.append(f"Row {row_idx} | {org} | {office} | missing={','.join(missing)}")

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_csv = export_dir / f"{input_csv.stem}_completed_{timestamp}.csv"
    log_file = export_dir / f"{input_csv.stem}_completed_{timestamp}_crawl_log.txt"

    with output_csv.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "Organisation",
                "Acronym",
                "Office",
                "Web Address (Url)",
                "Contact Person",
                "Email",
                "Focus Areas",
                "Contact Number",
                "Any other possibe contact email addresses",
            ],
            delimiter=";",
        )
        writer.writeheader()
        writer.writerows(rows)

    with log_file.open("w", encoding="utf-8") as f:
        f.write(f"Input: {input_csv}\n")
        f.write(f"Output: {output_csv}\n")
        f.write(f"Rows: {len(rows)}\n")
        f.write(f"Rows with unresolved D/F/H/I fields: {len(log_lines)}\n\n")
        for line in log_lines:
            f.write(line + "\n")

    return output_csv, log_file


def main() -> int:
    parser = argparse.ArgumentParser(description="Populate JW database contact columns via web crawling.")
    parser.add_argument(
        "--input",
        default=r"E:\ETDP\ETDP\Requests\JW_database_V1.csv",
        help="Input CSV path",
    )
    parser.add_argument(
        "--exports",
        default=r"E:\ETDP\ETDP\Exports",
        help="Export folder path",
    )
    args = parser.parse_args()

    input_csv = Path(args.input)
    export_dir = Path(args.exports)
    export_dir.mkdir(parents=True, exist_ok=True)

    output_csv, log_file = run(input_csv, export_dir)
    print(f"Completed CSV: {output_csv}")
    print(f"Crawl log: {log_file}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
