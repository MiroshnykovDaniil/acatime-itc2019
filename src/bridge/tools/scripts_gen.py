#!/usr/bin/env python3
"""Generates the C# constraint/estimation scripts for the ITC-2019 -> AcaTime encoding.

Every script is a Roslyn C# script compiled by AcaTime.ScriptExecutionService
(imports: System, System.Linq, System.Collections.Generic, AcaTime.ScriptModels;
referenced assemblies: core + AcaTime.ScriptModels only).

Shared metadata encoding (identical in converter / runner / scorer):
  - Season dates: BASE = first Monday; dayIndex = (slot.Date.Date - BASE).TotalDays;
    week = dayIndex / nrDays; dow = dayIndex % nrDays (nrDays=7, nrWeeks=14 for ITC tg).
  - Pair numbering: 1..K, K = number of distinct candidate start values (sorted asc);
    pair p -> raw start = STARTS[p-1].
  - A class == one GroupSubject (Id = class id), NumberOfLessons = meetings of one
    chosen candidate time, slots LessonNumber-ordered 1..m in builder order.
  - Candidates flattened into parallel arrays indexed by candidate id k; per class a
    contiguous [CL_OFF[ci], CL_OFF[ci+1]) range, XML order preserved.
  - Candidate arrays: C_ST (raw start), C_L, C_PEN, C_D (days bit 0 = Monday),
    C_W (weeks bit 0 = week 0).
  - Group ids: own group 2_000_000 + classId; hard SameAttendees cluster
    3_000_000..3_099_999 + i; hard NotOverlap cluster 3_100_000..3_199_999 + i.
    Teachers (synthetic, unique per class): 900_000 + classId.
    Classroom types (one per class): 700_000 + classId.

Solver-side (search-time) hard-constraint enforcement is CONSERVATIVE for the
class-level relation formulas: rejection happens if ANY pair of matching candidates
violates the formula. The scorer applies the exact official semantics on the final
chosen candidates.

Optional room-aware objective (converter --room-aware): the generated
itc-official-cost script also computes a greedy room assignment and adds room
penalties + a collision penalty. It is opt-in; the default generated script is
byte-identical to the pre-W5 one.
"""

BASE_DATE = (2025, 9, 1)  # start-of-season Monday

OWN_GROUP_BASE = 2_000_000
CLUSTER_SA_BASE = 3_000_000
CLUSTER_NO_BASE = 3_100_000
TEACHER_BASE = 900_000
CLASSROOM_TYPE_BASE = 700_000

W_OFFSET = 1_000_000            # estimation score = W_OFFSET - weighted penalties
PEN_OFFSET = W_OFFSET
UNKNOWN_PEN = 10_000_000        # per unsupported soft constraint

TYPE_CODES = {
    "SameAttendees": 0, "NotOverlap": 1, "SameTime": 2, "SameDays": 3,
    "DifferentDays": 4, "SameWeeks": 5, "DifferentWeeks": 6, "SameStart": 7,
    "DifferentTime": 8, "SameRoom": 9, "DifferentRoom": 10, "Precedence": 11,
    "Overlap": 12, "WorkDay": 30, "MinGap": 31,
    "MaxDays": 20, "MaxDayLoad": 21, "MaxBreaks": 22, "MaxBlock": 23,
}
PAIRWISE_TYPES = {tc for tc in TYPE_CODES.values() if tc not in (20, 21, 22, 23)}


def _fmt(arr, per_line=24):
    lines, cur = [], []
    for x in arr:
        cur.append(str(x))
        if len(cur) >= per_line:
            lines.append(", ".join(cur) + ",")
            cur = []
    if cur:
        lines.append(", ".join(cur))
    return "\n".join(lines)


def build_candidate_arrays(meta):
    """meta['classes'] = class dicts already in classId order; returns flat arrays."""
    classes = meta["classes"]
    cl_ids = [c["classId"] for c in classes]
    c_start, c_len, c_pen, c_d, c_w = [], [], [], [], []
    cl_off = [0]
    for c in classes:
        for t in c["times"]:
            assert len(t["days"]) == 7, "unsupported nrDays != 7"
            assert len(t["weeks"]) == meta["nrWeeks"], "weeks string length mismatch"
            c_start.append(int(t["start"]))
            c_len.append(int(t["length"]))
            c_pen.append(int(t.get("penalty", "0")))
            c_d.append(sum(1 << i for i, ch in enumerate(t["days"]) if ch == "1"))
            c_w.append(sum(1 << i for i, ch in enumerate(t["weeks"]) if ch == "1"))
        cl_off.append(len(c_start))
    return dict(cl_ids=cl_ids, c_start=c_start, c_len=c_len, c_pen=c_pen,
                c_d=c_d, c_w=c_w, cl_off=cl_off, starts=meta["starts"])


def _room_blocked(unavail, week, dow, start, length):
    """Same semantics as tools/roomsearch.py: unavailable window overlaps."""
    for u in unavail:
        if isinstance(u, dict):
            ud, us, ul, uw = u["days"], u["start"], u["length"], u["weeks"]
        else:
            ud, us, ul, uw = u
        if dow < len(ud) and week < len(uw) and ud[dow] == "1" and uw[week] == "1" \
                and start < us + ul and us < start + length:
            return True
    return False


def build_room_arrays(meta):
    """Room data for the ITC room-aware objective.

    The GA never assigns rooms in ITC mode (rooms are a post-pass), so fitness
    must reason about a *feasible room assignment for the current time
    structure* without persisting it. We encode:
      - per candidate: the list of allowed room indices (unavailability only;
        ITC FAQ I1: class limit is not compared with room capacity);
      - a 32-bit mask for small room sets (fast narrow path);
      - per candidate: the set of pair slots whose start lies inside the
        meeting interval (room occupancy is checked/marked there);
      - per class/room: the official room penalty.
    The generated estimator greedily assigns rooms per date/pair cell, adds the
    room penalties, and charges a large penalty for each collision. It is a
    heuristic (a greedy interval/list assignment), so it is used only in the
    opt-in `--room-aware` conversion; the scorer stays the referee.
    """
    rooms = meta["rooms"]
    rids = sorted(int(r) for r in rooms)
    rix = {rid: i for i, rid in enumerate(rids)}
    n_rooms = len(rids)
    classes = meta["classes"]
    seed_rooms = meta.get("seed_rooms", {})
    seed_cands = meta.get("seed_cands", {})
    seed_room_idx, seed_cand_idx = [], []
    cl_off = [0]
    for c in classes:
        cl_off.append(cl_off[-1] + len(c["times"]))
    for ci, c in enumerate(classes):
        cid = c["classId"]
        rid = seed_rooms.get(cid)
        seed_room_idx.append(rix.get(int(rid), -1) if rid is not None else -1)
        local = int(seed_cands.get(cid, -1))
        # chosen[ci] is the GLOBAL flattened candidate index in generated code.
        seed_cand_idx.append(cl_off[ci] + local if local >= 0 else -1)

    has_room, clr_nrooms, clr_tier = [], [], []
    c_rmask, c_pcell = [], []
    c_rallow_off, c_rallow_idx = [0], []
    clr_pen = [0] * (len(classes) * n_rooms)
    for ci, c in enumerate(classes):
        has_room.append(1 if c["hasRoom"] else 0)
        clr_nrooms.append(len(c["rooms"]) if c["hasRoom"] else 0)
        # ordering tier: scarce room lists first; supports >31 rooms without
        # iterating hundreds of passes.
        nrooms = len(c["rooms"]) if c["hasRoom"] else 0
        clr_tier.append(0 if nrooms <= 1 else (1 if nrooms <= 5 else 2))
        for t in c["times"]:
            mask = 0
            allowed = []
            meetings = [(wi, di) for wi, chw in enumerate(t["weeks"]) if chw == "1"
                        for di, chd in enumerate(t["days"]) if chd == "1"]
            if c["hasRoom"]:
                for r in c["rooms"]:
                    rid = int(r["id"])
                    rt = rooms.get(str(rid)) or rooms.get(rid)
                    if rt is None:
                        continue
                    if any(_room_blocked(rt["unavailable"], w, d, t["start"], t["length"])
                           for (w, d) in meetings):
                        continue
                    ri = rix[rid]
                    allowed.append(ri)
                    if n_rooms <= 31:
                        mask |= 1 << ri
            c_rallow_off.append(len(c_rallow_idx))
            c_rallow_idx.extend(allowed)
            if n_rooms <= 31:
                c_rmask.append(mask)
            pc = 0
            for p, s in enumerate(meta["starts"]):
                if s >= t["start"] and s < t["start"] + t["length"]:
                    pc |= 1 << p
            c_pcell.append(pc)
        for r in c["rooms"]:
            rid = int(r["id"])
            if rid in rix:
                clr_pen[ci * n_rooms + rix[rid]] = int(r.get("penalty", 0))
    return dict(n_rooms=n_rooms, has_room=has_room, clr_nrooms=clr_nrooms,
                clr_tier=clr_tier, n_buckets=max(clr_nrooms, default=0) + 1,
                c_rmask=c_rmask, c_pcell=c_pcell, clr_pen=clr_pen,
                c_rallow_off=c_rallow_off, c_rallow_idx=c_rallow_idx,
                room_ids=rids, seed_room=seed_room_idx, seed_cand=seed_cand_idx)


def build_travel_arrays(meta, pin_rooms=None):
    """Travel per SameAttendees (hard+soft) class pair; only pairs with value>0.
    With incremental pinning, prior rooms are known: pinned-pinned pairs use the
    EXACT travel between prior rooms (conservative max-travel would over-reject
    legal gap-0 same-room/adjacent placements); pinned-free pairs use the max
    travel between the prior room and the free class's candidate rooms."""
    pr = pin_rooms or {}
    t_off = [0]
    t_id, t_val = [], []
    for c in meta["classes"]:
        for pid, val in sorted(c["travelMax"].items()):
            if val <= 0:
                continue
            v = val
            if c["classId"] in pr and pid in pr:
                v = meta["rooms"][str(pr[c["classId"]])]["travel"].get(str(pr[pid]), 0)
            elif c["classId"] in pr and c["rooms"]:
                v = max(meta["rooms"][str(pr[c["classId"]])]["travel"].get(str(r["id"]), 0)
                        for r in c["rooms"])
            elif pid in pr and c["rooms"]:
                v = max(meta["rooms"][str(pr[pid])]["travel"].get(str(r["id"]), 0)
                        for r in c["rooms"])
            if v > 0:
                t_id.append(pid)
                t_val.append(v)
        t_off.append(len(t_id))
    return dict(t_off=t_off, t_id=t_id, t_val=t_val)


def build_partner_arrays(meta):
    """All hard-constraint partner lists per class:
       sa (SameAttendees, with travel values), no (NotOverlap),
       st (SameTime), sd (SameDays). Partner entries store partner CLASS ids."""
    sa_off, sap_id, sap_v = [0], [], []
    no_off, nop_id = [0], []
    st_off, st_id = [0], []
    sd_off, sd_id = [0], []
    dd_off, dd_id = [0], []
    for c in meta["classes"]:
        for pid in sorted(c["saPartners"]):
            sap_id.append(pid)
            sap_v.append(c["travelMax"].get(pid, 0))
        nop_id.extend(c["noPartners"])
        st_id.extend(c["sameTimePartners"])
        sd_id.extend(c["sameDaysPartners"])
        dd_id.extend(c.get("ddPartners", []))
        sa_off.append(len(sap_id)); no_off.append(len(nop_id))
        st_off.append(len(st_id)); sd_off.append(len(sd_id))
        dd_off.append(len(dd_id))
    pr_ids = []
    for (a, b) in meta.get("precedencePairs", []):
        pr_ids.extend([a, b])
    return dict(sa_off=sa_off, sap_id=sap_id, sap_v=sap_v,
                no_off=no_off, nop_id=nop_id,
                st_off=st_off, st_id=st_id, sd_off=sd_off, sd_id=sd_id,
                dd_off=dd_off, dd_id=dd_id, pr_ids=pr_ids)


def build_pair_constraint_arrays(meta):
    """Soft and hard pairwise constraints as candidate-independent arrays.
    Returns dict with both soft (penalty) and hard (must-hold) pair lists as tuples
    (tc, ia, ib, pen, par) where ia/ib are class indexes."""
    soft, hard = [], []
    for d in meta["distributions"]:
        typ = d["type"].split("(", 1)[0]
        assert typ in TYPE_CODES, "unsupported distribution type %s" % typ
        tc = TYPE_CODES[typ]
        idxs = [meta["classIndexByClassId"][cid] for cid in d["classes"]]
        pen = int(d.get("penalty", "0"))
        par = int(d.get("param", 0) or 0)
        required = d["required"] is True or d.get("required", "false") == "true"
        if required:
            target = hard
        else:
            target = soft
        if tc in PAIRWISE_TYPES:
            for a in range(len(idxs)):
                for b in range(a + 1, len(idxs)):
                    target.append((tc, idxs[a], idxs[b], pen, par))
        # global types (MaxDays/MaxDayLoad/MaxBreaks/MaxBlock) are scorer-side only;
        # if present, the converter warns (estimator ignores them).
    return dict(soft=soft, hard=hard)


def _c_arrays(name, arr, typ="int"):
    if not arr:
        arr = []
    return "%s[] %s = new %s[] {\n%s\n};" % (typ, name, typ, _fmt(arr))


def _helper_block(meta, cm, travels, partners=None, pairs=None, cells=None,
                 extra_helpers=""):
    parts = []
    parts.append(_C_HELPERS_PRE.format(
        total_days=meta["nrWeeks"] * 7,
        base_year=BASE_DATE[0], base_month=BASE_DATE[1], base_day=BASE_DATE[2],
        n_classes=len(cm["cl_ids"]), n_cand=len(cm["c_start"]),
        cl_ids=_c_arrays("CL_IDS", cm["cl_ids"], "long"),
        cl_off=_c_arrays("CL_OFF", cm["cl_off"]),
        c_start=_c_arrays("C_ST", cm["c_start"]),
        c_len=_c_arrays("C_L", cm["c_len"]),
        c_pen=_c_arrays("C_PEN", cm["c_pen"]),
        c_d=_c_arrays("C_D", cm["c_d"]),
        c_w=_c_arrays("C_W", cm["c_w"]),
        starts=_c_arrays("STARTS", cm["starts"]),
        teacher_base=TEACHER_BASE, sa_base=CLUSTER_SA_BASE, no_base=CLUSTER_NO_BASE,
        own_base=OWN_GROUP_BASE,
    ))
    if partners:
        parts.append(_C_PARTNERS_ALL.format(
            sa_off=_c_arrays("SA_OFF", partners["sa_off"]),
            sap_id=_c_arrays("SAP_ID", partners["sap_id"], "long"),
            sap_v=_c_arrays("SAP_V", partners["sap_v"]),
            no_off=_c_arrays("NO_OFF", partners["no_off"]),
            nop_id=_c_arrays("NOP_ID", partners["nop_id"], "long"),
            st_off=_c_arrays("ST_OFF", partners["st_off"]),
            st_id=_c_arrays("ST_ID", partners["st_id"], "long"),
            sd_off=_c_arrays("SD_OFF", partners["sd_off"]),
            sd_id=_c_arrays("SD_ID", partners["sd_id"], "long"),
            dd_off=_c_arrays("DD_OFF", partners["dd_off"]),
            dd_id=_c_arrays("DD_ID", partners["dd_id"], "long"),
            pr_ids=_c_arrays("PR_IDS", partners["pr_ids"], "long")))
    if travels:
        parts.append(_C_TRAVELS.format(
            t_off=_c_arrays("T_OFF", travels["t_off"]),
            t_id=_c_arrays("T_ID", travels["t_id"]),
            t_val=_c_arrays("T_VAL", travels["t_val"])))
    if cells:
        parts.append(_C_CELLS.format(
            cl_celloff=_c_arrays("CL_CELLOFF", cells["cl_celloff"]),
            cellkey=_c_arrays("CELLKEY", cells["cellkey"]),
            cellm_off=_c_arrays("CELLM_OFF", cells["cellm_off"]),
            matchk=_c_arrays("MATCHK", cells["matchk"])))
        parts.append(_C_CELL_PARTNERS.format(
            cp_off=_c_arrays("CP_OFF", cells["cp_off"]),
            cp_id=_c_arrays("CP_ID", cells["cp_id"], "long")))
    if pairs is not None:
        def rows(prs):
            off, tc, ia, ib, pen, par = [0], [], [], [], [], []
            for p in prs:
                tc.append(p[0]); ia.append(p[1]); ib.append(p[2]); pen.append(p[3]); par.append(p[4])
                off.append(len(tc))
            return off, tc, ia, ib, pen, par
        so_off, so_tc, so_ia, so_ib, so_pen, so_par = rows(pairs["soft"])
        ha_off, ha_tc, ha_ia, ha_ib, ha_pen, ha_par = rows(pairs["hard"])
        parts.append(_C_PAIRS.format(
            so_off=_c_arrays("SO_OFF", so_off), so_tc=_c_arrays("SO_TC", so_tc),
            so_ia=_c_arrays("SO_IA", so_ia), so_ib=_c_arrays("SO_IB", so_ib),
            so_pen=_c_arrays("SO_PEN", so_pen), so_par=_c_arrays("SO_PAR", so_par),
            ha_off=_c_arrays("HA_OFF", ha_off), ha_tc=_c_arrays("HA_TC", ha_tc),
            ha_ia=_c_arrays("HA_IA", ha_ia), ha_ib=_c_arrays("HA_IB", ha_ib),
            ha_pen=_c_arrays("HA_PEN", ha_pen), ha_par=_c_arrays("HA_PAR", ha_par)))
    parts.append(_C_COVER)
    parts.append(_C_BITCOUNT)
    parts.append(extra_helpers)
    return "\n".join(parts)


_C_HELPERS_PRE = """DateTime BASE_ = new DateTime({base_year},{base_month},{base_day});
int N_CLASSES = {n_classes};
int N_CAND = {n_cand};
{cl_ids}
{cl_off}
{c_start}
{c_len}
{c_pen}
{c_d}
{c_w}
{starts}
int TOTAL_DAYS = {total_days};
int TB = {teacher_base};
int SA_BASE = {sa_base};
int NO_BASE = {no_base};
int OWN_BASE = {own_base};
Func<long,int> cix = (long id) => {{
    int lo = 0, hi = N_CLASSES - 1;
    while (lo < hi) {{ int mid = (lo + hi) / 2; if (CL_IDS[mid] < id) lo = mid + 1; else hi = mid; }}
    return CL_IDS[lo] == id ? lo : -1;
}};
Func<IScheduleSlot,int> dayIx = (IScheduleSlot s) => (int)(s.Date.Date - BASE_).TotalDays;
"""

_C_COVER = """
Func<int,int,int,int,bool> covers = (int k, int week, int dow, int p) => {{
    if (k < 0 || k >= N_CAND || p < 1 || p > STARTS.Length) return false;
    return C_ST[k] == STARTS[p - 1] && ((C_D[k] >> dow) & 1) != 0 && ((C_W[k] >> week) & 1) != 0;
}};
"""

_C_BITCOUNT = """
Func<int,int> BitCount = (int x) => {{ int c = 0; while (x != 0) {{ x &= x - 1; c++; }} return c; }};
Func<int,int,bool> Precedes = (int ka, int kb) => {{
    int fa = C_W[ka] & -C_W[ka], fb = C_W[kb] & -C_W[kb];
    int wa2 = BitCount(fa - 1), wb2 = BitCount(fb - 1);
    if (wa2 != wb2) return wa2 < wb2;
    int da2 = C_D[ka] & -C_D[ka], db2 = C_D[kb] & -C_D[kb];
    int dda2 = BitCount(da2 - 1), ddb2 = BitCount(db2 - 1);
    if (dda2 != ddb2) return dda2 < ddb2;
    return C_ST[ka] + C_L[ka] <= C_ST[kb];
}};
"""

_C_TRAVELS = """
{t_off}
{t_id}
{t_val}
Func<int,int,int> travOf = (int ia, int ib) => {{
    for (int j = T_OFF[ia]; j < T_OFF[ia + 1]; j++) if (T_ID[j] == ib) return T_VAL[j];
    return 0;
}};
"""

_C_PARTNERS_ALL = """
{sa_off}
{sap_id}
{sap_v}
{no_off}
{nop_id}
{st_off}
{st_id}
{sd_off}
{sd_id}
{dd_off}
{dd_id}
{pr_ids}
"""

_C_PAIRS = """
{so_off}
{so_tc}
{so_ia}
{so_ib}
{so_pen}
{so_par}
{ha_off}
{ha_tc}
{ha_ia}
{ha_ib}
{ha_pen}
{ha_par}
"""


# ----------------------------------------------------------------------------
# 1) Unitary constraint: per-slot allowed (date,pair) domain shaping
# ----------------------------------------------------------------------------

UNITARY_MAIN = """
(IScheduleSlot slot) => {{
    int ci = cix(slot.GroupSubject.Id);
    if (ci < 0) return false;
    int di = dayIx(slot);
    if (di < 0) return false;
    int week = di / 7, dow = di % 7, p = slot.PairNumber;
    if (p < 1 || p > STARTS.Length || week >= TOTAL_DAYS / 7) return false;
    int st = STARTS[p - 1];
    for (int k = CL_OFF[ci]; k < CL_OFF[ci + 1]; k++)
        if (C_ST[k] == st && ((C_D[k] >> dow) & 1) != 0 && ((C_W[k] >> week) & 1) != 0)
            return true;
    return false;
}}"""


def gen_unitary(meta, cm, travels):
    return (_helper_block(meta, cm, travels)
            + UNITARY_MAIN)


UNITARY_SELECTOR = "season => season.GroupSubjects.SelectMany(gs => gs.ScheduleSlots)"


# ----------------------------------------------------------------------------
# 2) Slot validator for ALL hard constraints:
#    SameAttendees / NotOverlap (partner classes via teacher index),
#    SameTime / SameDays partners, intra-class pattern consistency.
# ----------------------------------------------------------------------------

def gen_validator_hard(meta, cm, travels, partners, cells):
    cells = dict(cells)
    cp = build_cellpartner_arrays(meta, cm, cells)
    cells.update(cp)
    parts = _helper_block(meta, cm, travels, partners=partners, cells=cells)
    parts += gen_validator_hard.body
    return parts


def _load_template(name):
    import os
    p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "templates", name)
    with open(p) as f:
        return f.read()


gen_validator_hard.body = _load_template("validator_hard.cs")


# ----------------------------------------------------------------------------
# 4) Slot value estimation: candidate time penalty (guidance; higher = better)
# ----------------------------------------------------------------------------
VALUE_GUIDE_BODY = """
(IScheduleSlot slot, IAssignedSlots assigned) => {{
    int ci = cix(slot.GroupSubject.Id);
    if (ci < 0) return 0;
    int di = dayIx(slot);
    if (di < 0) return 0;
    int week = di / 7, dow = di % 7, p = slot.PairNumber;
    if (p < 1 || p > STARTS.Length) return 0;
    int st = STARTS[p - 1];
    int minPen = 0;
    bool found = false;
    for (int k = CL_OFF[ci]; k < CL_OFF[ci + 1]; k++)
        if (C_ST[k] == st && ((C_D[k] >> dow) & 1) != 0 && ((C_W[k] >> week) & 1) != 0)
        {{
            if (!found || C_PEN[k] < minPen) {{ minPen = C_PEN[k]; found = true; }}
        }}
    if (!found) return -100000;

    // anti-congestion spreading: penalize same-date partner meetings (spreads
    // SameAttendees/NotOverlap cluster members across days/weeks)
    int crowd = 0;
    var all = assigned.Slots;
    long myId = slot.GroupSubject.Id;
    for (int i = 0; i < all.Count; i++)
    {{
        var s2 = all[i];
        if (s2.Date.Date != slot.Date.Date) continue;
        int cj = cix(s2.GroupSubject.Id);
        if (cj < 0 || cj == ci) continue;
        if (IsPartner(ci, cj)) crowd++;
    }}
    return -minPen - 30 * crowd;
}}"""


def gen_value_guide(meta, cm, travels, cells=None):
    parts = _helper_block(meta, cm, travels)
    if cells is not None:
        parts += _C_CELLS.format(
            cl_celloff=_c_arrays("CL_CELLOFF", cells["cl_celloff"]),
            cellkey=_c_arrays("CELLKEY", cells["cellkey"]),
            cellm_off=_c_arrays("CELLM_OFF", cells["cellm_off"]),
            matchk=_c_arrays("MATCHK", cells["matchk"]))
    parts += _C_VALUE_PARTNERS.format(
        vp_off=_c_arrays("VP_OFF", meta["vp_off"]),
        vp_id=_c_arrays("VP_ID", meta["vp_id"], "long"))
    parts += VALUE_GUIDE_BODY
    return parts


# ----------------------------------------------------------------------------
# 5) Schedule estimation: official cost proxi (time penalties + soft distribution
#    penalty formulas, class-level, on the resolved chosen candidates).
#    score = 1_000_000 - w_time*Sigma(tPen) - w_dist*Sigma(pairPen) - 1e9*(mixed|hardVio) - 10M*unsupported
# ----------------------------------------------------------------------------

OFFICIAL_COST_BODY = """
(Func<IFacultySeason,int>)((season) => {{
    long timePen = 0, distPen = 0, unknownPen = 0;
    int nMixed = 0, nHardVio = 0;

    int[] chosen = new int[N_CLASSES];
    for (int i = 0; i < N_CLASSES; i++) chosen[i] = -1;

    foreach (var gs in season.GroupSubjects)
    {{
        int ci = cix(gs.Id);
        if (ci < 0) {{ nMixed++; continue; }}
        int m = gs.ScheduleSlots.Count;
        if (m == 0) continue;
        if (m > 400) {{ nMixed++; continue; }}
        for (int k = CL_OFF[ci]; k < CL_OFF[ci + 1]; k++)
        {{
            int dm = C_D[k], wm = C_W[k];
            if (BitCount(dm) * BitCount(wm) != m) continue;
            bool all = true;
            foreach (var slot in gs.ScheduleSlots)
            {{
                if (slot.Date == DateTime.MinValue) {{ all = false; break; }}
                int di = (int)(slot.Date.Date - BASE_).TotalDays;
                if (di < 0) {{ all = false; break; }}
                int w = di / 7, dd = di % 7;
                if (((dm >> dd) & 1) == 0 || ((wm >> w) & 1) == 0
                    || slot.PairNumber < 1 || slot.PairNumber > STARTS.Length
                    || C_ST[k] != STARTS[slot.PairNumber - 1])
                {{ all = false; break; }}
            }}
            if (all) {{ chosen[ci] = k; break; }}
        }}
        if (chosen[ci] < 0) nMixed++;
        else timePen += C_PEN[chosen[ci]];
    }}
{room_eval}
    for (int section = 0; section < 2; section++)
    {{
        int[] OFF = section == 0 ? SO_OFF : HA_OFF;
        int[] TC = section == 0 ? SO_TC : HA_TC;
        int[] IA = section == 0 ? SO_IA : HA_IA;
        int[] IB = section == 0 ? SO_IB : HA_IB;
        int[] PEN = section == 0 ? SO_PEN : HA_PEN;
        int[] PAR = section == 0 ? SO_PAR : HA_PAR;
        for (int j = 0; j < OFF[OFF.Length - 1]; j++)
        {{
            int tc = TC[j];
            int iaa = IA[j], ibb = IB[j];
            int ca = chosen[iaa], cb = chosen[ibb];
            if (ca < 0 || cb < 0) continue;
            bool violated = false;
            switch (tc)
            {{
                case 0: // SameAttendees (class-level, conservative travel)
                {{
                    int tv = travOf(iaa, ibb);
                    violated = (C_ST[ca] < C_ST[cb] + C_L[cb]) && (C_ST[cb] < C_ST[ca] + C_L[ca])
                        && ((C_D[ca] & C_D[cb]) != 0) && ((C_W[ca] & C_W[cb]) != 0)
                        && !((C_ST[ca] + C_L[ca] + tv <= C_ST[cb]) || (C_ST[cb] + C_L[cb] + tv <= C_ST[ca]));
                    break;
                }}
                case 1: // NotOverlap
                    violated = (C_ST[ca] < C_ST[cb] + C_L[cb]) && (C_ST[cb] < C_ST[ca] + C_L[ca])
                        && ((C_D[ca] & C_D[cb]) != 0) && ((C_W[ca] & C_W[cb]) != 0);
                    break;
                case 2: // SameTime (containment)
                    {{
                        int sa = C_ST[ca], ea = sa + C_L[ca], sb = C_ST[cb], eb = sb + C_L[cb];
                        violated = !((sa <= sb && eb <= ea) || (sb <= sa && ea <= eb));
                        break;
                    }}
                case 3: // SameDays (days subset)
                    {{
                        int or_ = C_D[ca] | C_D[cb];
                        violated = !(or_ == C_D[ca] || or_ == C_D[cb]);
                        break;
                    }}
                case 4: // DifferentDays
                    violated = (C_D[ca] & C_D[cb]) != 0;
                    break;
                case 5: // SameWeeks (weeks subset)
                    {{
                        int or_ = C_W[ca] | C_W[cb];
                        violated = !(or_ == C_W[ca] || or_ == C_W[cb]);
                        break;
                    }}
                case 6: // DifferentWeeks
                    violated = (C_W[ca] & C_W[cb]) != 0;
                    break;
                case 7: // SameStart
                    violated = C_ST[ca] != C_ST[cb];
                    break;
                case 8: // DifferentTime
                    violated = (C_ST[ca] < C_ST[cb] + C_L[cb]) && (C_ST[cb] < C_ST[ca] + C_L[ca]);
                    break;
                case 12: // Overlap
                    violated = (C_ST[ca] < C_ST[cb] + C_L[cb]) && (C_ST[cb] < C_ST[ca] + C_L[ca])
                        && ((C_D[ca] & C_D[cb]) != 0) && ((C_W[ca] & C_W[cb]) != 0);
                    break;
                case 11: // Precedence (first-meeting order)
                    {{
                        int fa = C_W[ca] & -C_W[ca];
                        int fb = C_W[cb] & -C_W[cb];
                        int wa2 = BitCount(fa - 1);
                        int wb2 = BitCount(fb - 1);
                        if (wa2 != wb2)
                            violated = !(wa2 < wb2);
                        else
                        {{
                            int da2 = C_D[ca] & -C_D[ca];
                            int db2 = C_D[cb] & -C_D[cb];
                            int dda2 = BitCount(da2 - 1);
                            int ddb2 = BitCount(db2 - 1);
                            if (dda2 != ddb2)
                                violated = !(dda2 < ddb2);
                            else
                                violated = !(C_ST[ca] + C_L[ca] <= C_ST[cb]);
                        }}
                        break;
                    }}
                case 30: // WorkDay(S): span of overlapping day+week pair
                    {{
                        int s = Math.Min(C_ST[ca], C_ST[cb]);
                        int e = Math.Max(C_ST[ca] + C_L[ca], C_ST[cb] + C_L[cb]);
                        violated = ((C_D[ca] & C_D[cb]) != 0) && ((C_W[ca] & C_W[cb]) != 0) && (e - s > PAR[j]);
                        break;
                    }}
                case 31: // MinGap(G)
                    {{
                        violated = ((C_D[ca] & C_D[cb]) != 0) && ((C_W[ca] & C_W[cb]) != 0)
                            && !((C_ST[ca] + C_L[ca] + PAR[j] <= C_ST[cb]) || (C_ST[cb] + C_L[cb] + PAR[j] <= C_ST[ca]));
                        break;
                    }}
                case 9: // SameRoom: rooms invisible to estimation; scorer accounts exactly
                case 10: // DifferentRoom
                    break;
                default:
                    unknownPen += (long){unsupported};
                    break;
            }}
            if (violated)
            {{
                if (section == 0) distPen += PEN[j];
                else nHardVio++;
            }}
        }}
    }}
    long score = ({w_offset}L - {w_time}L * timePen - {w_dist}L * distPen{room_terms}
                 - 1_000_000_000L * (nMixed + nHardVio) - unknownPen);
    if (score < int.MinValue) score = int.MinValue;
    return (int)score;
}})"""


ROOM_CONFLICT_PEN = 1_000_000

ROOM_ARRAYS = """
int N_ROOMS = {n_rooms};
int N_ROOM_BUCKETS = {n_buckets};
int[] ROOM_IDS = new int[] {{{room_ids}}};
int[] SEED_ROOM = new int[] {{{seed_room}}};
int[] SEED_CAND = new int[] {{{seed_cand}}};
int RoomIndexOf(long id) {{ for (int r = 0; r < N_ROOMS; r++) if (ROOM_IDS[r] == id) return r; return -1; }}
int SeedRoomOf(IGroupSubject gs) {{
    int seedRoom = -1;
    foreach (var slot in gs.ScheduleSlots) {{
        if (slot.Date == DateTime.MinValue || slot.ClassroomId == null) return -1;
        int ri = RoomIndexOf(slot.ClassroomId.Value);
        if (ri < 0) return -1;
        if (seedRoom < 0) seedRoom = ri;
        else if (seedRoom != ri) return -1;
    }}
    return seedRoom;
}}
{clr_has}
{clr_nrooms}
{c_rmask}
{c_pcell}
{clr_pen}
"""

ROOM_EVAL_BODY = """
    long roomPen = 0;
    int roomConf = 0;
    int[] occ = new int[TOTAL_DAYS * STARTS.Length];
    for (int pass = 0; pass < N_ROOM_BUCKETS; pass++)
    {
        foreach (var gs in season.GroupSubjects)
        {
            int ci = cix(gs.Id);
            if (ci < 0 || CLR_HAS[ci] == 0 || CLR_NROOMS[ci] != pass) continue;
            int k = chosen[ci];
            if (k < 0) continue;
            int rm = C_RMASK[k];
            int pc = C_PCELL[k];
            int seedRoom = (chosen[ci] == SEED_CAND[ci] && SEED_ROOM[ci] >= 0) ? SEED_ROOM[ci] : SeedRoomOf(gs);
            bool seedLocked = (chosen[ci] == SEED_CAND[ci] && SEED_ROOM[ci] >= 0);
            // Default W5 relaxation: assign a room per meeting. The final
            // roomsearch pass enforces the official one-room-per-class rule.
            foreach (var slot in gs.ScheduleSlots)
            {
                if (slot.Date == DateTime.MinValue) continue;
                int di = (int)(slot.Date.Date - BASE_).TotalDays;
                if (di < 0 || di >= TOTAL_DAYS) { roomConf++; continue; }
                if (rm == 0) { roomConf++; roomPen += 100000; continue; }
                int free = rm;
                for (int p = 0; p < STARTS.Length; p++)
                    if (((pc >> p) & 1) != 0) free &= ~occ[di * STARTS.Length + p];
                int pick = -1, bestPen = int.MaxValue;
                int pool = free != 0 ? free : rm;
                if (free == 0) roomConf++;
                for (int r = 0; r < N_ROOMS; r++)
                    if (((pool >> r) & 1) != 0)
                    {
                        int pen = CLR_PEN[ci * N_ROOMS + r];
                        if (pen < bestPen) { bestPen = pen; pick = r; }
                    }
                // A seed room hint is authoritative when it is still free for
                // this meeting. It avoids the greedy estimator rejecting an
                // already-feasible room assignment at the initial seed.
                if (seedRoom >= 0 && (seedLocked || ((rm >> seedRoom) & 1) != 0))
                {
                    bool seedFree = true;
                    for (int p = 0; p < STARTS.Length; p++)
                        if (((pc >> p) & 1) != 0 &&
                            (occ[di * STARTS.Length + p] & (1 << seedRoom)) != 0)
                        { seedFree = false; break; }
                    if (seedFree)
                    {
                        pick = seedRoom;
                        bestPen = CLR_PEN[ci * N_ROOMS + seedRoom];
                    }
                }
                if (pick < 0) { roomConf++; continue; }
                roomPen += bestPen;
                for (int p = 0; p < STARTS.Length; p++)
                    if (((pc >> p) & 1) != 0) occ[di * STARTS.Length + p] |= (1 << pick);
            }
        }
    }
"""

ROOM_EVAL_BODY_PER_CLASS = """
    long roomPen = 0;
    int roomConf = 0;
    int[] occ = new int[TOTAL_DAYS * STARTS.Length];
    for (int pass = 0; pass < N_ROOM_BUCKETS; pass++)
    {
        foreach (var gs in season.GroupSubjects)
        {
            int ci = cix(gs.Id);
            if (ci < 0 || CLR_HAS[ci] == 0 || CLR_NROOMS[ci] != pass) continue;
            int k = chosen[ci];
            if (k < 0) continue;
            int rm = C_RMASK[k];
            int pc = C_PCELL[k];
            int seedRoom = (chosen[ci] == SEED_CAND[ci] && SEED_ROOM[ci] >= 0) ? SEED_ROOM[ci] : SeedRoomOf(gs);
            bool seedLocked = (chosen[ci] == SEED_CAND[ci] && SEED_ROOM[ci] >= 0);
            // Official ITC assigns ONE room per class (all its meetings), not
            // one per meeting. Check the room against every meeting cell.
            int free = rm;
            bool seen = false;
            foreach (var slot in gs.ScheduleSlots)
            {
                if (slot.Date == DateTime.MinValue) continue;
                int di = (int)(slot.Date.Date - BASE_).TotalDays;
                if (di < 0 || di >= TOTAL_DAYS) continue;
                seen = true;
                for (int p = 0; p < STARTS.Length; p++)
                    if (((pc >> p) & 1) != 0) free &= ~occ[di * STARTS.Length + p];
            }
            if (!seen) continue;
            if (rm == 0) { roomConf++; roomPen += 100000; continue; }
            int pick = -1, bestPen = int.MaxValue;
            int pool = free != 0 ? free : rm;
            if (free == 0) roomConf++;
            for (int r = 0; r < N_ROOMS; r++)
                if (((pool >> r) & 1) != 0)
                {
                    int pen = CLR_PEN[ci * N_ROOMS + r];
                    if (pen < bestPen) { bestPen = pen; pick = r; }
                }
            if (seedRoom >= 0 && (seedLocked || ((rm >> seedRoom) & 1) != 0))
            {
                bool seedFree = true;
                foreach (var slot in gs.ScheduleSlots)
                {
                    if (slot.Date == DateTime.MinValue) continue;
                    int di2 = (int)(slot.Date.Date - BASE_).TotalDays;
                    if (di2 < 0 || di2 >= TOTAL_DAYS) continue;
                    for (int p = 0; p < STARTS.Length; p++)
                        if (((pc >> p) & 1) != 0 &&
                            (occ[di2 * STARTS.Length + p] & (1 << seedRoom)) != 0)
                        { seedFree = false; break; }
                    if (!seedFree) break;
                }
                if (seedFree)
                {
                    pick = seedRoom;
                    bestPen = CLR_PEN[ci * N_ROOMS + seedRoom];
                }
            }
            if (pick < 0) { roomConf++; continue; }
            roomPen += bestPen;
            foreach (var slot in gs.ScheduleSlots)
            {
                if (slot.Date == DateTime.MinValue) continue;
                int di = (int)(slot.Date.Date - BASE_).TotalDays;
                if (di < 0 || di >= TOTAL_DAYS) continue;
                for (int p = 0; p < STARTS.Length; p++)
                    if (((pc >> p) & 1) != 0) occ[di * STARTS.Length + p] |= (1 << pick);
            }
        }
    }
"""

ROOM_ARRAYS_WIDE = """
int N_ROOMS = {n_rooms};
int ROOM_WORDS = {room_words};
int[] ROOM_IDS = new int[] {{{room_ids}}};
int[] SEED_ROOM = new int[] {{{seed_room}}};
int[] SEED_CAND = new int[] {{{seed_cand}}};
int RoomIndexOf(long id) {{ for (int r = 0; r < N_ROOMS; r++) if (ROOM_IDS[r] == id) return r; return -1; }}
int SeedRoomOf(IGroupSubject gs) {{
    int seedRoom = -1;
    foreach (var slot in gs.ScheduleSlots) {{
        if (slot.Date == DateTime.MinValue || slot.ClassroomId == null) return -1;
        int ri = RoomIndexOf(slot.ClassroomId.Value);
        if (ri < 0) return -1;
        if (seedRoom < 0) seedRoom = ri;
        else if (seedRoom != ri) return -1;
    }}
    return seedRoom;
}}
{clr_has}
{clr_tier}
{c_rallow_off}
{c_rallow_idx}
{c_pcell}
{clr_pen}
"""

ROOM_EVAL_BODY_WIDE = """
    long roomPen = 0;
    int roomConf = 0;
    ulong[] occ = new ulong[TOTAL_DAYS * STARTS.Length * ROOM_WORDS];
    for (int pass = 0; pass < 3; pass++)
    {
        foreach (var gs in season.GroupSubjects)
        {
            int ci = cix(gs.Id);
            if (ci < 0 || CLR_HAS[ci] == 0 || CLR_TIER[ci] != pass) continue;
            int k = chosen[ci];
            if (k < 0) continue;
            int pc = C_PCELL[k];
            int aoff = C_RALLOW_OFF[k];
            int aend = C_RALLOW_OFF[k + 1];
            int seedRoom = (chosen[ci] == SEED_CAND[ci] && SEED_ROOM[ci] >= 0) ? SEED_ROOM[ci] : SeedRoomOf(gs);
            bool seedLocked = (chosen[ci] == SEED_CAND[ci] && SEED_ROOM[ci] >= 0);
            foreach (var slot in gs.ScheduleSlots)
            {
                if (slot.Date == DateTime.MinValue) continue;
                int di = (int)(slot.Date.Date - BASE_).TotalDays;
                if (di < 0 || di >= TOTAL_DAYS) { roomConf++; continue; }
                if (aoff >= aend) { roomConf++; roomPen += 100000; continue; }
                int pick = -1, bestPen = int.MaxValue;
                // free candidate rooms
                for (int a = aoff; a < aend; a++)
                {
                    int r = C_RALLOW_IDX[a];
                    ulong bit = 1UL << (r & 63);
                    bool free = true;
                    for (int p = 0; p < STARTS.Length; p++)
                        if (((pc >> p) & 1) != 0 &&
                            (occ[(di * STARTS.Length + p) * ROOM_WORDS + (r >> 6)] & bit) != 0)
                        { free = false; break; }
                    if (!free) continue;
                    int pen = CLR_PEN[ci * N_ROOMS + r];
                    if (pen < bestPen) { bestPen = pen; pick = r; }
                }
                if (seedRoom >= 0)
                {
                    bool seedAllowed = seedLocked;
                    if (!seedLocked)
                        for (int a = aoff; a < aend; a++)
                            if (C_RALLOW_IDX[a] == seedRoom) { seedAllowed = true; break; }
                    if (seedAllowed)
                    {
                        ulong sbit = 1UL << (seedRoom & 63);
                        bool seedFree = true;
                        for (int p = 0; p < STARTS.Length; p++)
                            if (((pc >> p) & 1) != 0 &&
                                (occ[(di * STARTS.Length + p) * ROOM_WORDS + (seedRoom >> 6)] & sbit) != 0)
                            { seedFree = false; break; }
                        if (seedFree)
                        {
                            pick = seedRoom;
                            bestPen = CLR_PEN[ci * N_ROOMS + seedRoom];
                        }
                    }
                }
                if (pick < 0)
                {
                    roomConf++;
                    // blocked: pick the cheapest allowed room for diagnostics
                    for (int a = aoff; a < aend; a++)
                    {
                        int r = C_RALLOW_IDX[a];
                        int pen = CLR_PEN[ci * N_ROOMS + r];
                        if (pen < bestPen) { bestPen = pen; pick = r; }
                    }
                }
                if (pick < 0) { roomConf++; continue; }
                roomPen += bestPen;
                ulong pbit = 1UL << (pick & 63);
                for (int p = 0; p < STARTS.Length; p++)
                    if (((pc >> p) & 1) != 0)
                        occ[(di * STARTS.Length + p) * ROOM_WORDS + (pick >> 6)] |= pbit;
            }
        }
    }
"""

ROOM_EVAL_BODY_WIDE_PER_CLASS = """
    long roomPen = 0;
    int roomConf = 0;
    ulong[] occ = new ulong[TOTAL_DAYS * STARTS.Length * ROOM_WORDS];
    for (int pass = 0; pass < 3; pass++)
    {
        foreach (var gs in season.GroupSubjects)
        {
            int ci = cix(gs.Id);
            if (ci < 0 || CLR_HAS[ci] == 0 || CLR_TIER[ci] != pass) continue;
            int k = chosen[ci];
            if (k < 0) continue;
            int pc = C_PCELL[k];
            int aoff = C_RALLOW_OFF[k];
            int aend = C_RALLOW_OFF[k + 1];
            bool seen = false;
            foreach (var slot in gs.ScheduleSlots)
                if (slot.Date != DateTime.MinValue) seen = true;
            if (!seen) continue;
            if (aoff >= aend) { roomConf++; roomPen += 100000; continue; }

            int seedRoom = (chosen[ci] == SEED_CAND[ci] && SEED_ROOM[ci] >= 0) ? SEED_ROOM[ci] : SeedRoomOf(gs);
            bool seedLocked = (chosen[ci] == SEED_CAND[ci] && SEED_ROOM[ci] >= 0);
            int pick = -1, bestPen = int.MaxValue;

            // Seed hint: use the externally room-feasible assignment when it is
            // still legal for the current candidate.
            if (seedRoom >= 0)
            {
                bool allowed = seedLocked;
                if (!seedLocked)
                    for (int a = aoff; a < aend; a++)
                        if (C_RALLOW_IDX[a] == seedRoom) { allowed = true; break; }
                if (allowed)
                {
                    ulong sbit = 1UL << (seedRoom & 63);
                    bool free = true;
                    foreach (var slot in gs.ScheduleSlots)
                    {
                        if (slot.Date == DateTime.MinValue) continue;
                        int di = (int)(slot.Date.Date - BASE_).TotalDays;
                        if (di < 0 || di >= TOTAL_DAYS) { free = false; break; }
                        for (int p = 0; p < STARTS.Length; p++)
                            if (((pc >> p) & 1) != 0 &&
                                (occ[(di * STARTS.Length + p) * ROOM_WORDS + (seedRoom >> 6)] & sbit) != 0)
                            { free = false; break; }
                        if (!free) break;
                    }
                    if (free)
                    {
                        pick = seedRoom;
                        bestPen = CLR_PEN[ci * N_ROOMS + seedRoom];
                    }
                }
            }

            // Fallback: one room for ALL meetings, not one room per meeting.
            if (pick < 0)
            {
                for (int a = aoff; a < aend; a++)
                {
                    int r = C_RALLOW_IDX[a];
                    ulong bit = 1UL << (r & 63);
                    bool free = true;
                    foreach (var slot in gs.ScheduleSlots)
                    {
                        if (slot.Date == DateTime.MinValue) continue;
                        int di = (int)(slot.Date.Date - BASE_).TotalDays;
                        if (di < 0 || di >= TOTAL_DAYS) { free = false; break; }
                        for (int p = 0; p < STARTS.Length; p++)
                            if (((pc >> p) & 1) != 0 &&
                                (occ[(di * STARTS.Length + p) * ROOM_WORDS + (r >> 6)] & bit) != 0)
                            { free = false; break; }
                        if (!free) break;
                    }
                    if (!free) continue;
                    int pen = CLR_PEN[ci * N_ROOMS + r];
                    if (pen < bestPen) { bestPen = pen; pick = r; }
                }
            }
            if (pick < 0)
            {
                roomConf++;
                for (int a = aoff; a < aend; a++)
                {
                    int r = C_RALLOW_IDX[a];
                    int pen = CLR_PEN[ci * N_ROOMS + r];
                    if (pen < bestPen) { bestPen = pen; pick = r; }
                }
            }
            if (pick < 0) { roomConf++; continue; }
            roomPen += bestPen;
            ulong pbit = 1UL << (pick & 63);
            foreach (var slot in gs.ScheduleSlots)
            {
                if (slot.Date == DateTime.MinValue) continue;
                int di = (int)(slot.Date.Date - BASE_).TotalDays;
                if (di < 0 || di >= TOTAL_DAYS) continue;
                for (int p = 0; p < STARTS.Length; p++)
                    if (((pc >> p) & 1) != 0)
                        occ[(di * STARTS.Length + p) * ROOM_WORDS + (pick >> 6)] |= pbit;
            }
        }
    }
"""

ROOM_SCORE_TERMS = " - 1L * roomPen - %dL * roomConf" % ROOM_CONFLICT_PEN
# wide instances have tens of thousands of meeting collisions; 1e6 per
# collision would saturate the int estimate and erase the gradient.
ROOM_CONFLICT_PEN_WIDE = 10_000
ROOM_SCORE_TERMS_WIDE = " - 1L * roomPen - %dL * roomConf" % ROOM_CONFLICT_PEN_WIDE


def gen_official_cost(meta, cm, travels, pairs, w_time, w_dist, room_aware=False, room_exact=False):
    parts = _helper_block(meta, cm, travels, pairs=pairs)
    if room_aware:
        ra = build_room_arrays(meta)
        if ra["n_rooms"] <= 31:
            parts += "\n" + ROOM_ARRAYS.format(
                n_rooms=ra["n_rooms"],
                n_buckets=ra["n_buckets"],
                room_ids=", ".join(str(x) for x in ra["room_ids"]),
                seed_room=", ".join(str(x) for x in ra["seed_room"]),
                seed_cand=", ".join(str(x) for x in ra["seed_cand"]),
                clr_has=_c_arrays("CLR_HAS", ra["has_room"]),
                clr_nrooms=_c_arrays("CLR_NROOMS", ra["clr_nrooms"]),
                c_rmask=_c_arrays("C_RMASK", ra["c_rmask"]),
                c_pcell=_c_arrays("C_PCELL", ra["c_pcell"]),
                clr_pen=_c_arrays("CLR_PEN", ra["clr_pen"]))
            room_body = ROOM_EVAL_BODY_PER_CLASS if room_exact else ROOM_EVAL_BODY
            room_terms = ROOM_SCORE_TERMS
        else:
            room_words = (ra["n_rooms"] + 63) // 64
            parts += "\n" + ROOM_ARRAYS_WIDE.format(
                n_rooms=ra["n_rooms"],
                room_words=room_words,
                room_ids=", ".join(str(x) for x in ra["room_ids"]),
                seed_room=", ".join(str(x) for x in ra["seed_room"]),
                seed_cand=", ".join(str(x) for x in ra["seed_cand"]),
                clr_has=_c_arrays("CLR_HAS", ra["has_room"]),
                clr_tier=_c_arrays("CLR_TIER", ra["clr_tier"]),
                c_rallow_off=_c_arrays("C_RALLOW_OFF", ra["c_rallow_off"]),
                c_rallow_idx=_c_arrays("C_RALLOW_IDX", ra["c_rallow_idx"]),
                c_pcell=_c_arrays("C_PCELL", ra["c_pcell"]),
                clr_pen=_c_arrays("CLR_PEN", ra["clr_pen"]))
            room_body = (ROOM_EVAL_BODY_WIDE_PER_CLASS if room_exact
                         else ROOM_EVAL_BODY_WIDE)
            room_terms = ROOM_SCORE_TERMS_WIDE
        body = OFFICIAL_COST_BODY.format(
            unsupported=UNKNOWN_PEN, w_offset=W_OFFSET, w_time=w_time, w_dist=w_dist,
            room_eval="__ROOM_EVAL__", room_terms="__ROOM_TERMS__")
        body = body.replace("__ROOM_EVAL__", room_body)
        body = body.replace("__ROOM_TERMS__", room_terms)
        if ra["n_rooms"] > 31:
            # large instances: scale hard-violation penalties so the int
            # estimate remains informative (the scorer still decides).
            body = body.replace(
                "- 1_000_000_000L * (nMixed + nHardVio)",
                "- 100_000L * (nMixed + nHardVio)")
    else:
        body = OFFICIAL_COST_BODY.format(
            unsupported=UNKNOWN_PEN, w_offset=W_OFFSET, w_time=w_time, w_dist=w_dist,
            room_eval="", room_terms="")
    return parts + body


# ----------------------------------------------------------------------------
# 6) Slot priority (MRV): room-scarcity + hard-partner density + domain size.
#    Carriers on the own group: CourseYearId = candidate-room count (30 for
#    roomless classes), EducationalProgramId = number of hard partner classes.
#    Higher score = selected first (ResortFirstK sorts descending).
# ----------------------------------------------------------------------------

SLOT_PRIORITY_BODY = """
(Func<ISlotEstimation,int>)((sp) => {{
    var g = sp.GroupSubject.Groups.First();
    int rooms = (int)g.CourseYearId;
    int partners = (int)g.EducationalProgramId;
    return -10 * sp.AvailableDomains - 100 * rooms + {pw} * partners;
}})"""

# high-partner classes first: the giant SA/NO cliques bind late otherwise and
# explode backtracking at the end of the search (measured: depth wall ~200/550)
PRIORITY_PARTNER_WEIGHT = -5


def gen_slot_priority(meta, cm, travels):
    return _helper_block(meta, cm, travels) + SLOT_PRIORITY_BODY.format(pw=PRIORITY_PARTNER_WEIGHT)


# ----------------------------------------------------------------------------
# 7) Cell-index arrays: for each class, its distinct covered (week*7+dow) cells
#    (sorted) with the matched candidate ids per cell (XML order).
# ----------------------------------------------------------------------------

def build_cell_arrays(meta, cm):
    classes = meta["classes"]
    cl_celloff = [0]
    cellkey = []
    cellm_off = [0]
    matchk = []
    for c in classes:
        by_cell = {}
        for k in range(cm["cl_off"][c["index"]], cm["cl_off"][c["index"] + 1]):
            t = c["times"][k - cm["cl_off"][c["index"]]]
            for wi, chw in enumerate(t["weeks"]):
                if chw == "1":
                    for di, chd in enumerate(t["days"]):
                        if chd == "1":
                            by_cell.setdefault(wi * 7 + di, []).append(k)
        for key in sorted(by_cell):
            cellkey.append(key)
            cellm_off.append(cellm_off[-1] + len(by_cell[key]))
            matchk.extend(by_cell[key])
        cl_celloff.append(len(cellkey))
    return dict(cl_celloff=cl_celloff, cellkey=cellkey,
                cellm_off=cellm_off, matchk=matchk)


_C_CELLS = """
{cl_celloff}
{cellkey}
{cellm_off}
{matchk}
Func<int,int,int> cellRange = (int ci, int key) => {{
    int lo = CL_CELLOFF[ci], hi = CL_CELLOFF[ci + 1] - 1;
    while (lo <= hi) {{ int mid = (lo + hi) / 2; if (CELLKEY[mid] < key) lo = mid + 1; else if (CELLKEY[mid] > key) hi = mid - 1; else return mid; }}
    return -1;
}};
"""

_C_CELL_PARTNERS = """
{cp_off}
{cp_id}
"""


def build_cellpartner_arrays(meta, cm, cells):
    """Per (class, cell): hard SameAttendees/NotOverlap partners whose candidates
    cover that same cell. The validator only needs partners at the candidate's
    own date (same-date witness); violations through other common cells are
    witnessed by the other slot's own check."""
    classes = meta["classes"]
    # per class: set of cells (keys) it covers -> reuse cells["cellkey"] ranges
    cover = []  # cover[ci] = set of keys
    for c in classes:
        lo, hi = cells["cl_celloff"][c["index"]], cells["cl_celloff"][c["index"] + 1]
        cover.append(set(cells["cellkey"][lo:hi]))
    idx_of = meta["classIndexByClassId"]
    partners_of = [set(c["saPartners"]) | set(c["noPartners"]) for c in classes]
    cp_off = [0]
    cp_id = []
    for c in classes:
        for key in cells["cellkey"][cells["cl_celloff"][c["index"]]:
                                     cells["cl_celloff"][c["index"] + 1]]:
            for p in sorted(partners_of[c["index"]]):
                if key in cover[idx_of[p]]:
                    cp_id.append(p)
            cp_off.append(len(cp_id))
    return dict(cp_off=cp_off, cp_id=cp_id)


_C_VALUE_PARTNERS = """
{vp_off}
{vp_id}
Func<int,int,bool> IsPartner = (int a, int b) => {{
    int lo = VP_OFF[a], hi = VP_OFF[a + 1] - 1;
    long bid = CL_IDS[b];
    while (lo <= hi) {{ int mid = (lo + hi) / 2; if (VP_ID[mid] < bid) lo = mid + 1; else if (VP_ID[mid] > bid) hi = mid - 1; else return true; }}
    return false;
}};
"""


def build_value_partner_arrays(meta):
    vp_off = [0]
    vp_id = []
    for c in meta["classes"]:
        vp_id.extend(sorted(set(c["saPartners"]) | set(c["noPartners"])))
        vp_off.append(len(vp_id))
    return dict(vp_off=vp_off, vp_id=vp_id)
