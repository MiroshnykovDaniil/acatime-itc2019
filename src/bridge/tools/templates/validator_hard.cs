// Template: hard-constraint slot validator, cell-index optimized.
// Match lists per (class, week*7+dow cell) come from CL_CELLOFF/CELLKEY/
// CELLM_OFF/MATCHK; loops only touch candidates that can actually cover the
// cells involved, and filter start by stp/stq.

(IScheduleSlot slot, IAssignedSlots assigned) => {{
    int ia = cix(slot.GroupSubject.Id);
    if (ia < 0) return false;
    int da = dayIx(slot);
    if (da < 0) return false;
    int wa = da / 7, dda = da % 7, pa = slot.PairNumber;
    if (pa < 1 || pa > STARTS.Length) return false;
    int stp = STARTS[pa - 1];
    int keyA = wa * 7 + dda;
    int ra = cellRange(ia, keyA);
    if (ra < 0) return false;
    if (ra < 0) return false;

    // intra-class pattern consistency: any two meetings must be coverable by one candidate
    var mine = assigned.GetSlotsByTeacher(slot.GroupSubject.Teacher.Id);
    foreach (var s2 in mine)
    {{
        int db = (int)(s2.Date.Date - BASE_).TotalDays;
        int wb = db / 7, ddb = db % 7, pb = s2.PairNumber;
        if (pb < 1 || pb > STARTS.Length) return false;
        int stq = STARTS[pb - 1];
        int rb = cellRange(ia, wb * 7 + ddb);
        if (rb < 0) return false;
        if (ra < 0) return false;
        bool ok = false;
        for (int i = CELLM_OFF[ra]; i < CELLM_OFF[ra + 1]; i++)
        {{
            int k = MATCHK[i];
            if (C_ST[k] != stp) continue;
            for (int i2 = CELLM_OFF[rb]; i2 < CELLM_OFF[rb + 1]; i2++)
            {{
                int k2 = MATCHK[i2];
                if (C_ST[k2] != stq) continue;
                if (k2 == k) {{ ok = true; break; }}
            }}
            if (ok) break;
        }}
        if (!ok) return false;
    }}

    // SameDays hard partners: days-subset (conservative)
    for (int j = SD_OFF[ia]; j < SD_OFF[ia + 1]; j++)
    {{
        int ib = cix(SD_ID[j]);
        if (ib < 0) return false;
        var others = assigned.GetSlotsByTeacher(TB + CL_IDS[ib]);
        if (others.Count == 0) continue;
        if (ra < 0) return false;
        foreach (var s2 in others)
        {{
            int db = (int)(s2.Date.Date - BASE_).TotalDays;
            int rb = cellRange(ib, (db / 7) * 7 + db % 7);
            if (rb < 0) continue;
            int pb = s2.PairNumber;
            if (pb < 1 || pb > STARTS.Length) continue;
            int stq = STARTS[pb - 1];
            bool pairOk = false;
            for (int x = CELLM_OFF[ra]; x < CELLM_OFF[ra + 1] && !pairOk; x++)
            {{
                int ka = MATCHK[x];
                if (C_ST[ka] != stp) continue;
                int dbits = C_D[ka];
                for (int y = CELLM_OFF[rb]; y < CELLM_OFF[rb + 1]; y++)
                {{
                    int kb = MATCHK[y];
                    if (C_ST[kb] != stq) continue;
                    int or_ = dbits | C_D[kb];
                    if (or_ == dbits || or_ == C_D[kb]) {{ pairOk = true; break; }}
                }}
            }}
            if (!pairOk) return false;
        }}
    }}

    // SameTime hard partners: containment (conservative)
    for (int j = ST_OFF[ia]; j < ST_OFF[ia + 1]; j++)
    {{
        int ib = cix(ST_ID[j]);
        if (ib < 0) return false;
        var others = assigned.GetSlotsByTeacher(TB + CL_IDS[ib]);
        if (others.Count == 0) continue;
        if (ra < 0) return false;
        foreach (var s2 in others)
        {{
            int db = (int)(s2.Date.Date - BASE_).TotalDays;
            int rb = cellRange(ib, (db / 7) * 7 + db % 7);
            if (rb < 0) continue;
            int pb = s2.PairNumber;
            if (pb < 1 || pb > STARTS.Length) continue;
            int stq = STARTS[pb - 1];
            bool pairOk = false;
            for (int x = CELLM_OFF[ra]; x < CELLM_OFF[ra + 1] && !pairOk; x++)
            {{
                int ka = MATCHK[x];
                if (C_ST[ka] != stp) continue;
                int sa = C_ST[ka], ea = sa + C_L[ka];
                for (int y = CELLM_OFF[rb]; y < CELLM_OFF[rb + 1]; y++)
                {{
                    int kb = MATCHK[y];
                    if (C_ST[kb] != stq) continue;
                    int sb = C_ST[kb], eb = sb + C_L[kb];
                    if ((sa <= sb && eb <= ea) || (sb <= sa && ea <= eb)) {{ pairOk = true; break; }}
                }}
            }}
            if (!pairOk) return false;
        }}
    }}

    // NotOverlap hard partners: same-date witness (date-anchored formula; violations
    // through other common cells are caught by those cells' own slot checks)
    {{
        if (ra < 0) return false;
        for (int p = CP_OFF[ra]; p < CP_OFF[ra + 1]; p++)
        {{
            int ib = cix(CP_ID[p]);
            if (ib < 0) return false;
            var others = assigned.GetSlotsByTeacherAndDate(TB + CL_IDS[ib], slot.Date.Date);
            foreach (var s2 in others)
            {{
                int pb = s2.PairNumber;
                if (pb < 1 || pb > STARTS.Length) continue;
                int stq = STARTS[pb - 1];
                int rb = cellRange(ib, keyA);
                if (rb < 0) continue;
                for (int x = CELLM_OFF[ra]; x < CELLM_OFF[ra + 1]; x++)
                {{
                    int ka = MATCHK[x];
                    if (C_ST[ka] != stp) continue;
                    int sa = C_ST[ka], ea = sa + C_L[ka];
                    for (int y = CELLM_OFF[rb]; y < CELLM_OFF[rb + 1]; y++)
                    {{
                        int kb = MATCHK[y];
                        if (C_ST[kb] != stq) continue;
                        int sb = C_ST[kb], eb = sb + C_L[kb];
                        if (sa < eb && sb < ea) return false;
                    }}
                }}
            }}
        }}
    }}

    // DifferentDays hard partners: days bitsets must be disjoint (weeks irrelevant)
    for (int j = DD_OFF[ia]; j < DD_OFF[ia + 1]; j++)
    {{
        int ib = cix(DD_ID[j]);
        if (ib < 0) return false;
        var others = assigned.GetSlotsByTeacher(TB + CL_IDS[ib]);
        foreach (var s2 in others)
        {{
            int db = (int)(s2.Date.Date - BASE_).TotalDays;
            int wb = db / 7, ddb = db % 7, pb = s2.PairNumber;
            int stq = pb >= 1 && pb <= STARTS.Length ? STARTS[pb - 1] : -1;
            bool ddOk = false;
            for (int ka = CL_OFF[ia]; ka < CL_OFF[ia + 1] && !ddOk; ka++)
            {{
                if (C_ST[ka] != stp || ((C_W[ka] >> wa) & 1) == 0 || ((C_D[ka] >> dda) & 1) == 0) continue;
                for (int kb = CL_OFF[ib]; kb < CL_OFF[ib + 1]; kb++)
                {{
                    if (C_ST[kb] != stq || ((C_W[kb] >> wb) & 1) == 0 || ((C_D[kb] >> ddb) & 1) == 0) continue;
                    if ((C_D[ka] & C_D[kb]) == 0) {{ ddOk = true; break; }}
                }}
            }}
            if (!ddOk) return false;
        }}
    }}

    // Precedence hard partners: ordered first-meeting relation (PR_IDS = a,b pairs)
    for (int j = 0; j + 1 < PR_IDS.Length; j += 2)
    {{
        int iaa = cix(PR_IDS[j]), ibb = cix(PR_IDS[j + 1]);
        if (iaa < 0 || ibb < 0) return false;
        if (iaa != ia && ibb != ia) continue;
        int iFirst = (iaa == ia) ? ia : ibb;
        int iSecond = (iaa == ia) ? ibb : ia;
        bool slotIsFirst = (iaa == ia);
        int rFirst = slotIsFirst ? ra : -1;
        var others = assigned.GetSlotsByTeacher(TB + (slotIsFirst ? CL_IDS[ibb] : CL_IDS[iaa]));
        foreach (var s2 in others)
        {{
            int db = (int)(s2.Date.Date - BASE_).TotalDays;
            int wB = db / 7, ddB = db % 7, pB = s2.PairNumber;
            if (pB < 1 || pB > STARTS.Length) continue;
            int stq2 = STARTS[pB - 1];
            int rSecond = cellRange(iSecond, wB * 7 + ddB);
            if (rSecond < 0) continue;
            int rA = slotIsFirst ? ra : rSecond;
            int rB = slotIsFirst ? rSecond : ra;
            int stA = slotIsFirst ? stp : stq2;
            int stB = slotIsFirst ? stq2 : stp;
            bool prOk = false;
            for (int x = CELLM_OFF[rA]; x < CELLM_OFF[rA + 1] && !prOk; x++)
            {{
                int ka = MATCHK[x];
                if (C_ST[ka] != stA) continue;
                for (int y = CELLM_OFF[rB]; y < CELLM_OFF[rB + 1]; y++)
                {{
                    int kb = MATCHK[y];
                    if (C_ST[kb] != stB) continue;
                    if (Precedes(ka, kb)) {{ prOk = true; break; }}
                }}
            }}
            if (!prOk) return false;
        }}
    }}

    // SameAttendees hard partners: same-date witness with travel gap (conservative)
    {{
        if (ra < 0) return false;
        for (int p = CP_OFF[ra]; p < CP_OFF[ra + 1]; p++)
        {{
            int ib = cix(CP_ID[p]);
            if (ib < 0) return false;
            int tv = travOf(ia, ib);
            var others = assigned.GetSlotsByTeacherAndDate(TB + CL_IDS[ib], slot.Date.Date);
            foreach (var s2 in others)
            {{
                int pb = s2.PairNumber;
                if (pb < 1 || pb > STARTS.Length) continue;
                int stq = STARTS[pb - 1];
                int rb = cellRange(ib, keyA);
                if (rb < 0) continue;
                for (int x = CELLM_OFF[ra]; x < CELLM_OFF[ra + 1]; x++)
                {{
                    int ka = MATCHK[x];
                    if (C_ST[ka] != stp) continue;
                    int sa = C_ST[ka], ea = sa + C_L[ka];
                    for (int y = CELLM_OFF[rb]; y < CELLM_OFF[rb + 1]; y++)
                    {{
                        int kb = MATCHK[y];
                        if (C_ST[kb] != stq) continue;
                        int sb = C_ST[kb], eb = sb + C_L[kb];
                        if (!((ea + tv <= sb) || (eb + tv <= sa))) return false;
                    }}
                }}
            }}
        }}
    }}
    return true;
}}
