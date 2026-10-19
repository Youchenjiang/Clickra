import json,collections,sys,os
_dir = os.path.dirname(os.path.abspath(__file__))
d=json.load(open(os.path.join(_dir, 'l5.json'), encoding='utf-8'));N=d['nodes'];E=[tuple(e) for e in d['edges']]
def solve(fo,E):
    FE=collections.Counter((fo(a),fo(b)) for a,b in E if fo(a)!=fo(b))
    fs=sorted({fo(n) for n in N}); k=len(fs)
    W=[[FE.get((fs[i],fs[j]),0) for j in range(k)] for i in range(k)]
    INF=10**9;dp=[INF]*(1<<k);par=[None]*(1<<k);dp[0]=0
    for m in range(1<<k):
        if dp[m]==INF:continue
        for j in range(k):
            if m>>j&1:continue
            c=dp[m]+sum(W[j][s] for s in range(k) if m>>s&1)
            if c<dp[m|1<<j]:dp[m|1<<j]=c;par[m|1<<j]=j
    m=(1<<k)-1;order=[]
    while m: j=par[m];order.append(fs[j]);m^=1<<j
    order=order[::-1];pos={f:i for i,f in enumerate(order)}
    return order,[(a,b) for a,b in E if fo(a)!=fo(b) and pos[fo(b)]<pos[fo(a)]]
MOVE=dict(x.split('=') for x in sys.argv[1:])
fo=lambda n: MOVE.get(n,N[n]['folder'])
order,back=solve(fo,E)
print(order);print(len(back))
for a,b in back: print(' ',fo(a),a,'->',fo(b),b)
