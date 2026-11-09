# ITC 2019 competition-reduced data

The files downloaded here are the **competition-reduced DTU versions** of the
ITC 2019 instances, not the raw competition XML and not the post-competition
reduction.

- Dataset DOI: <https://doi.org/10.11583/DTU.20014127>
- Dataset title: *ITC 2019 competition reduced datasets*
- Creator: Dennis Søren Holm, Technical University of Denmark
- These are the versions used in the published ITC 2019 comparisons.

## Files used by this artifact

| File | Instance | Classes | Rooms |
|---|---|---:|---:|
| `tg-fal17_reduced.xml` | Turkish-German University, Fall 2017 | 711 | 23 |
| `tg-spr18_reduced.xml` | Turkish-German University, Spring 2018 | 676 | 24 |
| `lums-fal17_reduced.xml` | Lahore University of Management Sciences, Fall 2017 | 502 | 97 |
| `lums-spr18_reduced.xml` | Lahore University of Management Sciences, Spring 2018 | 487 | 73 |
| `iku-fal17_reduced.xml` | İstanbul Kültür University, Fall 2017 | 2,641 | 214 |

`iku-fal17` is included for the negative/scope result; it is not a certified
GA success. `iku-spr18` is not downloaded because it is untried in the paper.

## Download and verify

```bash
./download.sh
```

The script downloads the official DTU files and verifies them against
`CHECKSUMS.sha256`. To verify an existing copy without downloading:

```bash
../scripts/check_data.sh
```

## Do not mix reductions

Do not replace these files with the post-competition reduction
(`10.11583/DTU.20014070`). The hard structure may be similar, but the soft
objective/search space is not identical, so comparisons with published ITC
2019 results require the competition-reduced files.

## License

The data remains under the DTU dataset's own license and citation terms. This
repository does not relicense it. Cite the dataset DOI when reusing the files.
