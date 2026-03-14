import json
import os
import re
import sys
import time
import urllib.request
import urllib.error
from collections import defaultdict
from datetime import datetime

BASE = os.environ.get("ETDP_BASE", "http://localhost:5299/api").rstrip("/")
QID = int(os.environ.get("ETDP_QID", "51"))
OUT_ROOT = os.environ.get("ETDP_OUT", rf"C:\ETDP\ETDP\Exports\90420\automap_{datetime.now().strftime('%Y%m%d_%H%M%S')}")
os.makedirs(OUT_ROOT, exist_ok=True)


def api(method, path, payload=None, timeout=300):
    url = f"{BASE}/{path.lstrip('/')}"
    data = None
    headers = {"Accept": "application/json"}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read()
            txt = raw.decode("utf-8") if raw else ""
            if not txt:
                return None
            return json.loads(txt)
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="ignore") if e.fp else ""
        raise RuntimeError(f"HTTP {e.code} {method} {path}: {body[:500]}")


def getv(d, *keys, default=None):
    for k in keys:
        if k in d and d[k] is not None:
            return d[k]
    return default


def to_int(v):
    if v is None:
        return None
    if isinstance(v, bool):
        return int(v)
    if isinstance(v, (int, float)):
        return int(v)
    s = str(v).strip()
    if not s:
        return None
    try:
        return int(float(s))
    except Exception:
        return None


def to_str(v):
    return "" if v is None else str(v)


def parse_lpn(v):
    m = re.search(r"\d+", to_str(v))
    return int(m.group(0)) if m else 999999


def is_blank(v):
    return not to_str(v).strip()


# validate qualification
quals = api("GET", "Qualification") or []
q = None
for qq in quals:
    if to_int(getv(qq, "id", "Id")) == QID:
        q = qq
        break
if not q:
    raise RuntimeError(f"Qualification id {QID} not found")
q_number = to_str(getv(q, "qualificationNumber", "QualificationNumber"))
q_desc = to_str(getv(q, "qualificationDescription", "QualificationDescription"))

subjects = api("GET", f"Subject/byQualification?qualificationId={QID}") or []
topics = api("GET", f"Topic/byQualification?qualificationId={QID}") or []
criteria = api("GET", f"AssessmentCriteria/byQualification?qualificationId={QID}") or []
toolkit_all = api("GET", "LecturerToolkit") or []

toolkit = []
for e in toolkit_all:
    if to_int(getv(e, "qualificationsId", "QualificationsId")) == QID:
        toolkit.append(e)

subject_by_id = {to_int(getv(s, "id", "Id")): s for s in subjects}
topic_by_id = {to_int(getv(t, "id", "Id")): t for t in topics}
criteria_by_id = {to_int(getv(c, "id", "Id")): c for c in criteria}

groups = defaultdict(list)
for e in toolkit:
    cid = to_int(getv(e, "assessmentCriteriaId", "AssessmentCriteriaId"))
    eid = to_int(getv(e, "id", "Id"))
    key = f"criteria:{cid}" if cid else f"entry:{eid}"
    groups[key].append(e)

results = []
groups_mapped = 0
groups_failed = 0
propagated_rows = 0

for gkey, entries in groups.items():
    entries = sorted(entries, key=lambda x: (parse_lpn(getv(x, "lpn", "Lpn")), to_int(getv(x, "id", "Id")) or 999999))
    if not entries:
        continue

    seed = entries[0]
    seed_id = to_int(getv(seed, "id", "Id"))
    cid = to_int(getv(seed, "assessmentCriteriaId", "AssessmentCriteriaId"))
    cobj = criteria_by_id.get(cid)
    tobj = topic_by_id.get(to_int(getv(cobj or {}, "topicId", "TopicId"))) if cobj else None
    sobj = subject_by_id.get(to_int(getv(tobj or {}, "subjectId", "SubjectId"))) if tobj else None

    subject_code = to_str(getv(sobj or {}, "subjectCode", "SubjectCode", default=getv(seed, "subjectCode", "SubjectCode")))
    subject_desc = to_str(getv(sobj or {}, "subjectDescription", "SubjectDescription", default=getv(seed, "subjectDescription", "SubjectDescription")))
    topic_desc = to_str(getv(tobj or {}, "topicDescription", "TopicDescription"))
    criteria_desc = to_str(getv(cobj or {}, "description", "Description", default=getv(seed, "assessmentCriteriaDescription", "AssessmentCriteriaDescription")))
    lesson_desc = to_str(getv(seed, "lessonPlanDescription", "LessonPlanDescription"))

    backend = "moderator"
    err = ""

    try:
        api("POST", "Content/moderator-insert-best-context", {
            "LecturerToolkitEntryId": seed_id,
            "Query": f"{q_number} {subject_code} {subject_desc} {topic_desc} {criteria_desc} {lesson_desc}",
            "QualificationCode": q_number,
            "QualificationDescription": q_desc,
            "SubjectDescription": subject_desc,
            "SubjectCode": subject_code,
            "TopicDescription": topic_desc,
            "AssessmentCriteriaDescription": criteria_desc,
            "LessonPlanDescription": lesson_desc,
            "Cite": False,
            "CandidateLimit": 8,
            "SnippetLength": 1600,
            "DryRun": False,
        })
    except Exception as ex:
        err = str(ex)

    try:
        seed_now = api("GET", f"LecturerToolkit/{seed_id}")
    except Exception:
        seed_now = seed

    final_content = to_str(getv(seed_now, "lessonPlanContent", "LessonPlanContent")).strip()

    if not final_content:
        backend = "draft_fallback"
        try:
            draft = api("POST", "Content/draft", {
                "SubjectName": subject_code,
                "SubjectDescription": subject_desc,
                "TopicDescription": topic_desc,
                "TopicPurpose": "",
                "LessonPlanDescription": lesson_desc,
                "AssessmentCriteriaDescription": criteria_desc,
                "LecturerActions": to_str(getv(seed, "lecturerActions", "LecturerActions")),
                "LearnerActions": to_str(getv(seed, "learnerActions", "LearnerActions")),
                "Sources": [],
                "Length": "600-900 words",
                "Level": "TVET NQF 4",
            })
            draft_text = to_str(getv(draft or {}, "content", "Content")).strip()
            if draft_text:
                api("POST", "Content/assemble", {"LecturerToolkitEntryId": seed_id, "Content": draft_text})
                seed_now = api("GET", f"LecturerToolkit/{seed_id}")
                final_content = to_str(getv(seed_now, "lessonPlanContent", "LessonPlanContent")).strip()
        except Exception as ex:
            err = f"{err} | fallback: {ex}" if err else str(ex)

    if not final_content:
        groups_failed += 1
        results.append({
            "group": gkey,
            "seedEntryId": seed_id,
            "status": "failed",
            "backend": backend,
            "error": err,
            "propagated": 0,
        })
        continue

    groups_mapped += 1
    grp_prop = 0

    for e in entries[1:]:
        eid = to_int(getv(e, "id", "Id"))
        existing = to_str(getv(e, "lessonPlanContent", "LessonPlanContent")).strip()
        if existing:
            continue

        payload = {
            "QualificationsId": to_int(getv(e, "qualificationsId", "QualificationsId")),
            "LearningInstitutionName": to_str(getv(e, "learningInstitutionName", "LearningInstitutionName")),
            "LecturerName": to_str(getv(e, "lecturerName", "LecturerName")),
            "SubjectCode": to_str(getv(e, "subjectCode", "SubjectCode")),
            "SubjectDescription": to_str(getv(e, "subjectDescription", "SubjectDescription")),
            "AssessmentCriteriaId": to_int(getv(e, "assessmentCriteriaId", "AssessmentCriteriaId")),
            "AssessmentCriteriaDescription": to_str(getv(e, "assessmentCriteriaDescription", "AssessmentCriteriaDescription")),
            "Lpn": to_str(getv(e, "lpn", "Lpn")),
            "LessonPlanDescription": to_str(getv(e, "lessonPlanDescription", "LessonPlanDescription")),
            "LessonPlanContent": final_content,
            "TimeStart": to_str(getv(e, "timeStart", "TimeStart")),
            "TimeEnd": to_str(getv(e, "timeEnd", "TimeEnd")),
            "LecturerActions": to_str(getv(e, "lecturerActions", "LecturerActions")),
            "LearnerActions": to_str(getv(e, "learnerActions", "LearnerActions")),
            "LearningAids": to_str(getv(e, "learningAids", "LearningAids")),
        }

        try:
            api("PUT", f"LecturerToolkit/{eid}", payload)
            grp_prop += 1
            propagated_rows += 1
        except Exception as ex:
            err = f"{err} | put:{eid}:{ex}" if err else f"put:{eid}:{ex}"

    results.append({
        "group": gkey,
        "seedEntryId": seed_id,
        "status": "mapped",
        "backend": backend,
        "error": err,
        "propagated": grp_prop,
    })

# final counts
fresh_toolkit_all = api("GET", "LecturerToolkit") or []
fresh_toolkit = [e for e in fresh_toolkit_all if to_int(getv(e, "qualificationsId", "QualificationsId")) == QID]
filled = sum(1 for e in fresh_toolkit if to_str(getv(e, "lessonPlanContent", "LessonPlanContent")).strip())

summary = {
    "qualificationId": QID,
    "qualificationNumber": q_number,
    "groupsTotal": len(groups),
    "groupsMapped": groups_mapped,
    "groupsFailed": groups_failed,
    "rowsPropagated": propagated_rows,
    "toolkitRows": len(fresh_toolkit),
    "toolkitRowsWithContent": filled,
    "toolkitRowsMissingContent": max(0, len(fresh_toolkit) - filled),
    "timestamp": datetime.utcnow().isoformat() + "Z",
}

summary_path = os.path.join(OUT_ROOT, "90420_automap_fill_summary.json")
results_path = os.path.join(OUT_ROOT, "90420_automap_fill_results.json")
with open(summary_path, "w", encoding="utf-8") as f:
    json.dump(summary, f, indent=2)
with open(results_path, "w", encoding="utf-8") as f:
    json.dump(results, f, indent=2)

print(json.dumps({
    "summary": summary,
    "summaryPath": summary_path,
    "resultsPath": results_path,
}, indent=2))
