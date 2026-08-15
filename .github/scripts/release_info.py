#!/usr/bin/env python3
"""릴리즈에 첨부할 package.json 을 만듭니다.

VCC 리스팅은 각 버전 항목에서 zip 다운로드 주소(url)와 무결성 해시(zipSHA256)를
요구합니다. 저장소의 package.json 에는 그 값이 없으므로 릴리즈 시점에 주입합니다.

사용법:
    release_info.py <package.json> <zip 파일> <다운로드 URL> <출력 경로>
"""
import hashlib
import json
import sys


def main(argv):
    if len(argv) != 5:
        print(__doc__, file=sys.stderr)
        return 2

    manifest_path, zip_path, url, out_path = argv[1:]

    with open(manifest_path, encoding="utf-8") as f:
        manifest = json.load(f)

    digest = hashlib.sha256()
    with open(zip_path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            digest.update(chunk)

    manifest["url"] = url
    manifest["zipSHA256"] = digest.hexdigest()

    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print("url        :", manifest["url"])
    print("zipSHA256  :", manifest["zipSHA256"])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
