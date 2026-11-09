.PHONY: help data check-data build test verify smoke

help:
	@echo "Targets:"
	@echo "  data        Download the DTU competition-reduced ITC 2019 instances"
	@echo "  check-data  Verify SHA-256 checksums of the downloaded data"
	@echo "  build       Build the .NET solution"
	@echo "  test        Run the .NET unit tests"
	@echo "  verify      Re-score all curated solutions and regenerate results/summary.csv"
	@echo "  smoke       Run only the 6,889 verification check"

data:
	./data/download.sh

check-data:
	./scripts/check_data.sh

build:
	dotnet build src/AcaTime.ItcArtifact.sln -c Release

test:
	dotnet test src/AcaTime.ItcArtifact.sln -c Release

verify:
	python3 scripts/verify_results.py

smoke:
	python3 scripts/verify_results.py --smoke
