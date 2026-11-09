.PHONY: help data check-data

help:
	@echo "Targets:"
	@echo "  data        Download the DTU competition-reduced ITC 2019 instances"
	@echo "  check-data  Verify SHA-256 checksums of the downloaded data"
	@echo ""
	@echo "Build/smoke/verify targets will be added with the code import."

data:
	./data/download.sh

check-data:
	./scripts/check_data.sh
