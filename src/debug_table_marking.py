import re

log_path = r"C:\Users\g1014308\.gemini\antigravity\brain\b49295cd-c23a-4dac-96fe-d39bee357687\.system_generated\tasks\task-17606.log"

with open(log_path, 'r', encoding='utf-8') as f:
    lines = f.readlines()

paragraphs = []
pattern = re.compile(r"\[(\d+)\]\s+([RL])\s+\[(\d+),(\d+),(\d+),(\d+)\]")

i = 0
while i < len(lines):
    line = lines[i].strip()
    match = pattern.match(line)
    if match:
        idx = int(match.group(1))
        col = match.group(2)
        x0, y0, x1, y1 = map(float, [match.group(3), match.group(4), match.group(5), match.group(6)])
        props = lines[i+1].strip()
        text = lines[i+2].strip()
        paragraphs.append({
            'idx': idx, 'col': col, 'x0': x0, 'y0': y0, 'x1': x1, 'y1': y1,
            'width': x1 - x0, 'height': y1 - y0, 'text': text, 'props': props
        })
        i += 3
    else:
        i += 1

print(f"Parsed {len(paragraphs)} paragraphs.")

def run_simulation(overlap_thresh_ratio):
    pageWidth = 595.0
    isTablePage = True
    candidates = []
    
    for para in paragraphs:
        txt = para['text'].strip()
        if not txt: continue
        if any(txt.startswith(w) for w in ["Table", "Figure", "Fig", "表", "圖"]): continue
        if txt.startswith("[") or "http" in txt.lower() or "doi" in txt.lower() or "www." in txt.lower() or re.search(r"\b10\.\d{4,}/", txt): continue
        if re.match(r"^(?:\d+|[a-zA-Z])\.$", txt) or re.match(r"^\((?:\d+|[a-zA-Z])\)$", txt) or re.match(r"^(?:\d+\.\s*)+$", txt): continue
        if re.match(r"^\d+(?:\.\d+)*\.?\s+[A-Z]", txt): continue
        if len(txt) <= 2 and not re.match(r"^[0-9✓xX-]$", txt): continue
        
        words = txt.split()
        if len(words) > 50: continue
        
        if para['width'] < pageWidth * 0.45 and para['height'] < 120:
            rowAlignedCount = 0
            colAlignedCount = 0
            
            for other in paragraphs:
                if other['idx'] == para['idx']: continue
                
                overlapY = min(para['y1'], other['y1']) - max(para['y0'], other['y0'])
                minHeight = min(para['height'], other['height'])
                if overlapY > minHeight * overlap_thresh_ratio:
                    rowAlignedCount += 1
                    
                overlapX = min(para['x1'], other['x1']) - max(para['x0'], other['x0'])
                minWidth = min(para['width'], other['width'])
                if overlapX > minWidth * 0.5:
                    colAlignedCount += 1
                    
            colAlignedOk = (colAlignedCount >= 1) or isTablePage
            rowStyleTable = isTablePage and colAlignedCount >= 2 and para['height'] < 35 and para['width'] > 80
            
            if (rowAlignedCount >= 1 and colAlignedOk) or rowStyleTable:
                candidates.append(para)

    filteredCandidates = []
    for cand in candidates:
        isRowStyle = isTablePage and cand['height'] < 35 and cand['width'] > 80
        hasNeighbor = False
        for other in candidates:
            if other['idx'] == cand['idx']: continue
            overlapY = min(cand['y1'], other['y1']) - max(cand['y0'], other['y0'])
            minH = min(cand['height'], other['height'])
            if overlapY > minH * overlap_thresh_ratio:
                overlapX = min(cand['x1'], other['x1']) - max(cand['x0'], other['x0'])
                if overlapX <= 0:
                    hasNeighbor = True
                    break
        if hasNeighbor or isRowStyle:
            filteredCandidates.append(cand)
            
    candidates = filteredCandidates
    
    # Grouping
    groups = []
    for cand in candidates:
        added = False
        for group in groups:
            close = False
            for member in group:
                center = pageWidth / 2
                candIsLeft = cand['x1'] <= center + 5
                memberIsLeft = member['x1'] <= center + 5
                if candIsLeft != memberIsLeft: continue
                verticalDist = 0
                if cand['y1'] < member['y0']:
                    verticalDist = member['y0'] - cand['y1']
                elif member['y1'] < cand['y0']:
                    verticalDist = cand['y0'] - member['y1']
                else:
                    verticalDist = 0
                    
                if verticalDist < 45:
                    close = True
                    break
            if close:
                group.append(cand)
                added = True
                break
        if not added:
            groups.append([cand])
            
    return groups

for ratio in [0.5, 0.15, 0.1]:
    groups = run_simulation(ratio)
    print(f"\n--- Simulation with ratio={ratio} ---")
    for gi, group in enumerate(groups):
        members = [p['idx'] for p in group]
        if any(idx in members for idx in [50, 57, 58, 65, 66, 74]):
            print(f"Group {gi}: size={len(group)}")
            print(f"  Members: {members}")
