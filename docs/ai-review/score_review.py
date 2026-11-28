#!/usr/bin/env python3
"""Предварительная разметка комментариев бота относительно эталонного списка.

    python docs/ai-review/score_review.py review.json [known_issues.json]

review.json — артефакт `ai-review` из GitHub Actions (или JSON из <details> комментария).

Сопоставление автоматическое и грубое: тот же файл и строка в пределах ±TOLERANCE от
строк эталонной проблемы (для TEST-1 — тот же файл/тестовый файл и правило TEST-*).
Итог ОБЯЗАТЕЛЬНО проверить вручную — колонка «авто» лишь подсказка.
"""

import json
import sys
from pathlib import Path

TOLERANCE = 2


def norm(path: str) -> str:
    return path.strip().lstrip("./").removeprefix("b/")


def near(line: int, lines: list[int], tol: int = TOLERANCE) -> bool:
    return any(abs(line - l) <= tol for l in lines if l) or (line == 0 and 0 in lines)


def match(comment: dict, issues: list[dict]) -> list[dict]:
    f, line, rule = norm(comment["file"]), int(comment.get("line") or 0), comment.get("rule", "")
    # модель иногда ошибается в номере строки на несколько строк, поэтому при совпадении
    # кода правила допускаем больший разброс
    found = [i for i in issues if norm(i["file"]) == f
             and (near(line, i["lines"]) or (i["rule"] == rule and near(line, i["lines"], 3 * TOLERANCE)))]
    if rule.startswith("TEST"):
        found += [i for i in issues if i["rule"].startswith("TEST") and i not in found
                  and (norm(i["file"]) == f or "Tests" in f)
                  and Path(norm(i["file"])).stem.replace("Controller", "") in comment.get("message", "") + f]
    # при нескольких кандидатах предпочитаем совпадение по правилу
    found.sort(key=lambda i: i["rule"] != rule)
    return found


def main() -> None:
    review_path = Path(sys.argv[1])
    gt_path = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(__file__).with_name("known_issues.json")
    comments = json.loads(review_path.read_text(encoding="utf-8")).get("comments") or []
    gt = json.loads(gt_path.read_text(encoding="utf-8"))
    issues, baits = gt["issues"], gt["baits"]

    found_ids: set[str] = set()
    rows, tp, dup, fp, bait_hits = [], 0, 0, 0, 0
    for n, c in enumerate(comments, 1):
        cand = match(c, issues)
        new = [i for i in cand if i["id"] not in found_ids]
        if new:
            verdict = f"TP → {new[0]['id']}"
            found_ids.add(new[0]["id"])
            tp += 1
        elif cand:
            verdict = f"дубль {cand[0]['id']}"
            dup += 1
        else:
            b = [b for b in baits if norm(b["file"]) == norm(c["file"]) and near(int(c.get("line") or 0), b["lines"])]
            verdict = f"FP (ловушка {b[0]['id']})" if b else "FP?"
            bait_hits += bool(b)
            fp += 1
        msg = c["message"].replace("|", "\\|")[:90]
        rows.append(f"| {n} | {c['severity']} | {c.get('rule','')} | `{norm(c['file'])}:{c.get('line',0)}` | {msg} | {verdict} |")

    total_known = len(issues)
    reported = tp + fp  # дубли не считаем ни в TP, ни в FP
    precision = tp / reported if reported else 0.0
    recall = len(found_ids) / total_known if total_known else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0

    print("| # | severity | rule | место | сообщение бота | авто |")
    print("|---|---|---|---|---|---|")
    print("\n".join(rows))
    print()
    print("Не найдено:", ", ".join(f"{i['id']} ({i['rule']})" for i in issues if i["id"] not in found_ids) or "—")
    print()
    print(f"Комментариев бота: {len(comments)} (TP {tp}, дублей {dup}, FP {fp}, из них на ловушках {bait_hits}/{len(baits)})")
    print(f"Precision = {tp}/{reported} = {precision:.2f}")
    print(f"Recall    = {len(found_ids)}/{total_known} = {recall:.2f}")
    print(f"F1        = {f1:.2f}")


if __name__ == "__main__":
    main()
