"""Legt für jede zugewiesene, offene Linear-Aufgabe im Team VIB automatisch einen Branch von `development` an
und schreibt den Branchnamen als Kommentar in die Aufgabe.

Branchname: steht in der Aufgabe unter "**Branch:** `...`", sonst <präfix>/<VIB-n>-<titel>.
Präfix: Label "Bug" -> fix/, Label "Improvement" -> rework/, sonst feature/.
"""
import json
import os
import re
import sys
import urllib.request

TEAM = "VIB"
BASE = "development"

QUERY = """
query($team: String!) {
  issues(first: 100, filter: {
    team: { key: { eq: $team } }
    assignee: { null: false }
    state: { type: { in: ["unstarted", "started"] } }
  }) {
    nodes { id identifier title description labels { nodes { name } } }
  }
}
"""

COMMENT = """mutation($issueId: String!, $body: String!) {
  commentCreate(input: { issueId: $issueId, body: $body }) { success }
}"""


def slug(title):
    t = title.lower()
    for a, b in (("ä", "ae"), ("ö", "oe"), ("ü", "ue"), ("ß", "ss")):
        t = t.replace(a, b)
    return re.sub(r"[^a-z0-9]+", "-", t).strip("-")[:40].strip("-")


def branch_name(identifier, title, labels, description=""):
    m = re.search(r"\*\*Branch:\*\*\s*`([^`]+)`", description or "")
    if m:
        return m.group(1)
    names = {l.lower() for l in labels}
    prefix = "fix" if "bug" in names else "rework" if "improvement" in names else "feature"
    return f"{prefix}/{identifier}-{slug(title)}"


def request(url, token, data=None, method=None, auth_prefix="Bearer "):
    req = urllib.request.Request(url, method=method, data=json.dumps(data).encode() if data else None)
    req.add_header("Authorization", auth_prefix + token)
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/vnd.github+json")
    with urllib.request.urlopen(req) as r:
        return json.load(r)


def linear(query, variables):
    res = request("https://api.linear.app/graphql", os.environ["LINEAR_API_KEY"],
                  {"query": query, "variables": variables}, auth_prefix="")
    if res.get("errors"):
        sys.exit(f"Linear-API-Fehler: {res['errors']}")
    return res["data"]


def main():
    if not os.environ.get("LINEAR_API_KEY"):
        sys.exit("Secret LINEAR_API_KEY fehlt (Repo → Settings → Secrets and variables → Actions).")
    repo, gh_token = os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_TOKEN"]
    api = f"https://api.github.com/repos/{repo}"

    branches = [r["ref"][len("refs/heads/"):] for r in request(f"{api}/git/matching-refs/heads", gh_token)]
    base_sha = request(f"{api}/git/ref/heads/{BASE}", gh_token)["object"]["sha"]

    for issue in linear(QUERY, {"team": TEAM})["issues"]["nodes"]:
        ident = issue["identifier"]
        pattern = re.compile(rf"(^|/){re.escape(ident)}(-|$)", re.IGNORECASE)
        if any(pattern.search(b) for b in branches):
            continue
        labels = [l["name"] for l in issue["labels"]["nodes"]]
        name = branch_name(ident, issue["title"], labels, issue["description"])
        request(f"{api}/git/refs", gh_token, {"ref": f"refs/heads/{name}", "sha": base_sha}, "POST")
        branches.append(name)
        linear(COMMENT, {"issueId": issue["id"], "body":
                         f"Branch `{name}` wurde automatisch von `{BASE}` erstellt.\n\n"
                         f"```\ngit fetch origin\ngit checkout {name}\n```"})
        print(f"{ident}: {name} angelegt")


def selftest():
    assert slug("Zugriffsbeschränkung für Dateien!") == "zugriffsbeschraenkung-fuer-dateien"
    assert branch_name("VIB-2", "Dateisystem", [], "**Branch:** `feature/VIB-2-dateisystem`") == "feature/VIB-2-dateisystem"
    assert branch_name("VIB-30", "Shell stürzt ab", ["Bug"]) == "fix/VIB-30-shell-stuerzt-ab"
    assert branch_name("VIB-31", "Shell aufräumen", ["Improvement"]) == "rework/VIB-31-shell-aufraeumen"
    assert branch_name("VIB-32", "Neues Feature", ["Grundlagen"]) == "feature/VIB-32-neues-feature"
    print("selftest ok")


if __name__ == "__main__":
    selftest() if "--selftest" in sys.argv else main()
