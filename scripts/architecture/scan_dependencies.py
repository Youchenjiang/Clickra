"""Whole-repo file graph: every .cs under src/ (obj/bin excluded); edge a->b when a uses a type declared in b."""
import os,re,json,collections
script_dir = os.path.dirname(os.path.abspath(__file__))
repo_root = os.path.abspath(os.path.join(script_dir, "..", ".."))
root = os.path.join(repo_root, "src")
decl=re.compile(r'^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|file)\s+)*(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+|unsafe\s+)*(?:class|interface|record|enum|struct|delegate\s+[\w<>\[\],\s]+?)\s+(?:struct\s+|class\s+)?(\w+)',re.M)
def code(s):
    s=re.sub(r'/\*.*?\*/','',s,flags=re.S); s=re.sub(r'//.*','',s)
    return re.sub(r'@?"(?:[^"\\\n]|\\.)*"','""',s)
files={}
for dp,_,fs in os.walk(root):
    dp_norm = dp.replace('\\', '/')
    if '/obj' in dp_norm or '/bin' in dp_norm: continue
    for f in fs:
        if f.endswith('.cs'):
            p=os.path.join(dp,f); raw=open(p,encoding='utf-8',errors='ignore').read()
            rel_p = os.path.relpath(p,root).replace('\\', '/')
            files[rel_p]=(code(raw),raw.count('\n')+1)
owner=collections.defaultdict(set); own=collections.defaultdict(set); ispartial=collections.defaultdict(set)
privdecl=re.compile(r'\bprivate\s+(?:static\s+|readonly\s+|sealed\s+|unsafe\s+)*(?:record\s+struct|record\s+class|class|struct|record|enum|interface|delegate\s+[\w<>\[\],\s]+?)\s+(\w+)')
private=set()
pdecl=re.compile(r'\bpartial\s+(?:class|record|struct|interface)\s+(\w+)')
for p,(s,_) in files.items():
    for t in decl.findall(s): owner[t].add(p); own[p].add(t)
    for t in pdecl.findall(s): ispartial[t].add(p)
    for t in privdecl.findall(s): private.add((t,p))
proj=lambda p: p.split('/')[0]
def visible(p,q):   # project references: CLI and Fluent reference Core; Shared is linked into Core and Shell
    return proj(p)==proj(q) or proj(q) in ('Clickra.Core','Shared') and proj(p)!='ClickraShell' or (proj(q)=='Shared' and proj(p)=='ClickraShell')
names=sorted(owner,key=len,reverse=True)
pat=re.compile(r'\b('+'|'.join(map(re.escape,names))+r')\b')
edges=set()
for p,(s,_) in files.items():
    for t in set(pat.findall(s)):
        if t in own[p] and t not in ispartial: continue   # its own (possibly duplicated private) type
        for q in owner[t]:
            if q!=p and (t,q) not in private and visible(p,q): edges.add((p,q))
# partial classes: same type in many files -> edges between them are noise; mark partial groups
partial={t:sorted(v) for t,v in ispartial.items() if len(v)>1}
dup={t:sorted(v) for t,v in owner.items() if len(v)>1 and t not in partial}
out_json_path = os.path.join(script_dir, 'all.json')
json.dump({'files':{p:n for p,(_,n) in files.items()},'edges':sorted(edges),'partial':partial,'dup':dup},open(out_json_path,'w',encoding='utf-8'),indent=0)
print(len(files),'files',len(edges),'edges','partial',{t:len(v) for t,v in partial.items()},'dup',dup)
print('short names',[t for t in owner if len(t)<=6])
iso=[p for p in files if not any(p in e for e in edges)]
print('isolated',len(iso),iso[:20])
