#!/usr/bin/env python3
"""릴리즈들에서 모은 package.json 여러 개를 VCC 용 VPM 리스팅(index.json)으로 합칩니다.

결과 형태:
    {
      "name": ..., "id": ..., "url": <이 index.json 의 주소>, "author": ...,
      "packages": { "<패키지 id>": { "versions": { "1.0.0": {<package.json 전체>}, ... } } }
    }

버전은 semver 내림차순으로 정렬합니다. VCC 는 순서에 의존하지 않지만
사람이 index.json 을 열어 볼 때 최신이 위에 오는 편이 읽기 좋습니다.

사용법:
    build_listing.py <index URL> <출력 파일> <package.json ...>
"""
import json
import re
import sys


def sort_key(version):
    """1.10.0 이 1.9.0 보다 뒤에 오도록 숫자로 비교합니다. 프리릴리즈는 정식보다 앞."""
    match = re.match(r"^(\d+)\.(\d+)\.(\d+)(?:-(.+))?$", version)
    if not match:
        return (0, 0, 0, 0, version)

    major, minor, patch, pre = match.groups()
    # 프리릴리즈가 있으면(1) 정식(2)보다 낮게 둡니다.
    return (int(major), int(minor), int(patch), 1 if pre else 2, pre or "")


def main(argv):
    if len(argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2

    index_url, out_path = argv[1], argv[2]
    manifest_paths = argv[3:]

    packages = {}
    skipped = []

    for path in manifest_paths:
        with open(path, encoding="utf-8") as f:
            manifest = json.load(f)

        name = manifest.get("name")
        version = manifest.get("version")
        if not name or not version:
            skipped.append((path, "name / version 이 없습니다"))
            continue
        if not manifest.get("url"):
            # url 이 없으면 VCC 가 내려받을 수 없습니다. 조용히 넣지 말고 알립니다.
            skipped.append((path, "url 이 없습니다 (릴리즈 자산이 오래된 형식일 수 있습니다)"))
            continue

        versions = packages.setdefault(name, {})
        if version in versions:
            skipped.append((path, "이미 있는 버전 " + version))
            continue
        versions[version] = manifest

    listing = {
        "name": "Exhibit Descriptor",
        "id": "com.gwanryo.exhibit-descriptor.listing",
        "url": index_url,
        "author": "Ryo",
        "packages": {
            name: {
                "versions": {
                    version: versions[version]
                    for version in sorted(versions, key=sort_key, reverse=True)
                }
            }
            for name, versions in sorted(packages.items())
        },
    }

    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(listing, f, indent=2, ensure_ascii=False)
        f.write("\n")

    for name, versions in listing["packages"].items():
        print(name, "->", ", ".join(versions["versions"]))
    for path, reason in skipped:
        print("건너뜀:", path, "-", reason, file=sys.stderr)

    if not packages:
        print("::warning::리스팅에 담을 패키지가 없습니다. 아직 릴리즈가 없을 수 있습니다.")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
