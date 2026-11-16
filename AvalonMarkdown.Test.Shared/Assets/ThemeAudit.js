/* ==================== ThemeAudit probe (test asset) ====================
 * Injected by AVALON_THEME_AUDIT=1 (MainView.StartThemeAudit) to classify the
 * ThemeConfigViewModel-generated CSS vs renderer.css:
 *  - effective (reaches an element) / dead (ineffective) / pseudo-only /
 *    hardcoded (no ThemeConfig slider).
 * Uses an injected hidden "kitchen-sink" fixture so every selector has a target.
 * Logs each row to console as [TAUDIT] ONLY when the custom-theme-css text changes,
 * so a headless run captures the result once. The fixed overlay panel is a bonus for
 * interactive runs; the C# gate reads the console rows.
 * ================================================================================ */
(function(){
    'use strict';
    var FIX_ID = 'theme-audit-fixture';
    var PANEL_ID = 'theme-audit-panel';
    var suppressUntil = 0;
    var lastCssSig = '';

    function themeName(){ return (document.documentElement.className||'').indexOf('theme-light')>=0 ? 'light' : 'dark'; }
    function cssRules(sh){ try { return sh && sh.cssRules ? Array.prototype.slice.call(sh.cssRules) : []; } catch(e){ return []; } }
    function ownStyles(){
        var out={base:null,custom:null};
        try {
            for (var i=0;i<document.styleSheets.length;i++){
                var sh=document.styleSheets[i]; var st=sh && sh.ownerNode;
                if(!st || st.nodeName!=='STYLE') continue;
                if(!document.contains(st)) continue;
                var txt=st.textContent||'';
                if(st.id==='custom-theme-css'){ out.custom=sh; }
                else if(!out.base && txt.indexOf('--bg-primary')>=0){ out.base=sh; }
            }
        } catch(e){}
        return out;
    }

    function ensureFixture(){
        if (document.getElementById(FIX_ID)) return;
        var d=document.createElement('div');
        d.id=FIX_ID;
        d.setAttribute('aria-hidden','true');
        d.style.cssText='display:none !important; position:absolute; width:0; height:0; overflow:hidden;';
        d.innerHTML =
            '<h1>AuditH1</h1><h2>AuditH2</h2><h3>AuditH3</h3>' +
            '<p>para <a href="#">link</a> <strong>bold</strong> <em>ital</em> <code>inline</code></p>' +
            '<ul><li>li <code>li-code</code></li>' +
            '<li class="task-list-item"><input type="checkbox" checked/></li>' +
            '<li class="task-list-item"><input type="checkbox"/></li></ul>' +
            '<blockquote>blockquote</blockquote>' +
            '<table><thead><tr><th>th</th></tr></thead><tbody><tr><td>td</td></tr></tbody></table>' +
            '<img alt=""/><hr/>' +
            '<div class="footnotes"><a class="footnote-ref" href="#fn1">[1]</a></div>' +
            '<div class="katex-block">katex</div>' +
            '<div class="empty-placeholder">placeholder</div>' +
            '<div class="embed-container"><div class="embed-facade"><span class="embed-play"></span><span class="embed-label">l</span></div></div>' +
            '<div class="mermaid-container"><svg></svg></div>' +
            '<div class="puml-container puml-dark"><img alt=""/></div>' +
            '<div class="plot-container"><svg class="function-plot"></svg></div>' +
            '<div class="chat-block-wrap"><details class="chat-block">' +
            '<summary class="chat-summary"><span class="chat-chevron">▸</span>' +
            '<span class="chat-label">chat</span></summary>' +
            '<div class="chat-body">body</div></details>' +
            '<span class="chat-status chat-status-live"><span class="chat-status-ring"></span>' +
            '<span class="chat-status-logo"><svg viewBox="0 0 24 24"><path d="M0 0h24v24H0z"/></svg>' +
            '</span></span></div>' +
            '<pre class="hljs"><code class="hljs">' +
            '<span class="hljs-keyword">kw</span><span class="hljs-literal">lit</span>' +
            '<span class="hljs-symbol">sy</span><span class="hljs-name">nm</span>' +
            '<span class="hljs-built_in">bi</span><span class="hljs-type">ty</span>' +
            '<span class="hljs-class">cl</span><span class="hljs-number">42</span>' +
            '<span class="hljs-string">st</span><span class="hljs-meta-string">ms</span>' +
            '<span class="hljs-title">ti</span>' +
            '<span class="hljs-title class_">tc</span>' +
            '<span class="hljs-title class_ inherited__">tci</span>' +
            '<span class="hljs-params">pa</span><span class="hljs-comment">co</span><span class="hljs-quote">qu</span>' +
            '<span class="hljs-variable">va</span><span class="hljs-template-variable">tv</span>' +
            '<span class="hljs-attr">at</span><span class="hljs-attribute">atr</span><span class="hljs-meta">me</span>' +
            '<span class="hljs-tag">ta</span><span class="hljs-bullet">bu</span>' +
            '<span class="hljs-selector-attr">sa</span><span class="hljs-selector-class">sc</span>' +
            '<span class="hljs-selector-id">si</span><span class="hljs-selector-pseudo">sp</span>' +
            '<span class="hljs-selector-tag">stg</span><span class="hljs-section">se</span>' +
            '<span class="hljs-emphasis">em</span><span class="hljs-strong">stg2</span>' +
            '<span class="hljs-addition">ad</span><span class="hljs-deletion">de</span>' +
            '<span class="hljs-regexp">re</span><span class="hljs-template-tag">tt</span><span class="hljs-doctag">do</span>' +
            '<span class="hljs-link">li</span></code></pre>' +
            '<div class="code-block-wrapper">' +
            '<div class="code-header"><span class="code-lang">cs</span><span class="code-actions">' +
            '<button class="copy-btn">C</button><button class="resize-btn">+</button></span></div>' +
            '<pre><code><span class="hljs-keyword">kw2</span></code></pre></div>';
        document.body.appendChild(d);
    }

    function ensurePanel(){
        if (document.getElementById(PANEL_ID)) return;
        var p=document.createElement('div');
        p.id=PANEL_ID;
        p.style.cssText='position:fixed;top:6px;right:6px;width:400px;max-height:92vh;overflow:auto;'+
            'background:rgba(18,18,22,0.96);color:#e6e6e6;font:11px/1.45 Consolas,monospace;'+
            'border:1px solid #555;border-radius:6px;padding:8px 10px;z-index:2147483646;'+
            'box-shadow:0 4px 18px rgba(0,0,0,.5);';
        p.innerHTML='<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px;cursor:default;">'+
            '<b style="font-size:12px;">🎨 主题配置审计</b>'+
            '<span><button data-a="refresh" style="margin:0 2px;">↻</button>'+
            '<button data-a="close" style="margin:0 2px;">✕</button></span></div>'+
            '<div data-a="body"></div>';
        p.addEventListener('click',function(ev){
            var b=ev.target&&ev.target.getAttribute?ev.target.getAttribute('data-a'):null;
            if(b==='refresh'){ runAudit(); }
            else if(b==='close'){ p.style.display='none'; }
        });
        document.body.appendChild(p);
    }

    function stripTheme(s){ return ((s||'').trim()).replace(/^html\.theme-(dark|light)\s*/,''); }
    function queryMany(sel){ try { return Array.prototype.slice.call(document.querySelectorAll(sel)); } catch(e){ return []; } }
    function matchChunks(selectorText){
        var out=[], anyVirtual=false;
        var parts=(selectorText||'').split(',');
        for (var i=0;i<parts.length;i++){
            var s=stripTheme(parts[i]);
            if(!s) continue;
            if(s.indexOf('::')>=0){ anyVirtual=true; continue; }
            out.push(s);
        }
        return {chunks:out, virtual:anyVirtual};
    }
    function matchedEls(chunks){
        var els=[];
        for (var i=0;i<chunks.length;i++){
            var arr=queryMany(chunks[i]);
            for (var j=0;j<arr.length;j++){ if (els.indexOf(arr[j])<0) els.push(arr[j]); }
        }
        return els;
    }
    function authoredPropsOf(cssText){
        var props=[];
        var ob=(cssText||'').indexOf('{'); var cb=(cssText||'').lastIndexOf('}');
        if(ob<0||cb<ob) return props;
        var body=cssText.slice(ob+1,cb);
        var parts=body.split(';');
        for (var i=0;i<parts.length;i++){
            var c=parts[i].indexOf(':');
            if(c<=0) continue;
            var p=parts[i].slice(0,c).trim();
            if(p && props.indexOf(p)<0) props.push(p);
        }
        return props;
    }
    // consumers: (selector, prop) that read var(--name) — name is FULL "--foo".
    // Parses AUTHORED declarations from rule.cssText (CSSOM does not expand
    // shorthands that contain var(), so style-enumeration would miss them).
    function consumersFor(name, shs){
        var out=[], needle='var('+name+')';
        for (var si=0;si<shs.length;si++){
            var rs=cssRules(shs[si]);
            for (var i=0;i<rs.length;i++){
                var r=rs[i]; var css=r.cssText||'';
                var ob=css.indexOf('{'); var cb=css.lastIndexOf('}');
                if (ob<0||cb<ob||css.indexOf(needle)<0) continue;
                var parts=css.slice(ob+1,cb).split(';');
                for (var k=0;k<parts.length;k++){
                    var c=parts[k].indexOf(':');
                    if (c<=0) continue;
                    var prop=parts[k].slice(0,c).trim();
                    var val=parts[k].slice(c+1).trim();
                    if (prop && val.indexOf(needle)>=0) out.push({selector:r.selectorText||'', prop:prop});
                }
            }
        }
        return out;
    }

    var SHORTHAND2LONG={
        'background':'background-color','border':'border-top-color',
        'border-top':'border-top-color','border-bottom':'border-bottom-color',
        'border-left':'border-left-color','border-right':'border-right-color'
    };
    function measureProp(declProp){ return SHORTHAND2LONG[declProp] || declProp; }
    function sentinelFor(declProp){
        var m=measureProp(declProp);
        if (m==='color'||m==='background-color'||m.indexOf('color')>=0||m==='fill'||m==='stroke'||m==='box-shadow')
            return 'rgb(0,250,1)';
        if (m==='border-radius') return '41px';
        if (m==='text-decoration') return 'none';
        if (m==='font-size') return '3px';
        if (m==='line-height') return '7';
        if (m==='padding-top'||m==='margin-top') return '77px';
        if (m==='filter') return 'grayscale(1)';
        if (m==='font-style') return 'normal';
        if (m==='font-weight') return '400';
        return '0';
    }

    function countChanged(els, mp, mutate){
        var before=[], changed=0;
        for (var i=0;i<els.length;i++) before.push(window.getComputedStyle(els[i]).getPropertyValue(mp));
        mutate();
        for (var j=0;j<els.length && j<before.length;j++){
            var now=window.getComputedStyle(els[j]).getPropertyValue(mp);
            if (before[j]!==now) changed++;
        }
        return changed;
    }

    function getVar(rules, name, themeCls){
        for (var i=0;i<rules.length;i++){
            var r=rules[i];
            if (themeCls && (r.selectorText||'').indexOf(themeCls)<0) continue;
            if (r.style && r.style.getPropertyValue(name)!=='') return r.style.getPropertyValue(name).trim();
        }
        return null;
    }
    // read a variable value straight from the <style> text of a given theme block
    function cssVarInText(text, cls, name){
        try{
            var i=text.indexOf(cls); if(i<0) return null;
            var ob=text.indexOf('{', i); if(ob<0) return null;
            var cb=text.indexOf('}', ob); if(cb<ob) return null;
            var seg=text.slice(ob+1, cb);
            var re=new RegExp(name+'\\s*:\\s*([^;}]+)');
            var m=seg.match(re);
            return m? m[1].trim() : null;
        }catch(e){ return null; }
    }

    function runAudit(){
        ensureFixture();
        ensurePanel();
        var th=themeName();
        var body=document.querySelector('#'+PANEL_ID+' [data-a="body"]');
        if (!body) return;
        var ss=ownStyles();
        var cnt={eff:0,dead:0,virt:0,eq:0,hard:0};
        if (!ss.custom){
            body.innerHTML='<div style="color:#ffb454">未找到 &lt;style id=custom-theme-css&gt;。<br/>拖动任意滑块应用后重试。</div>';
            return;
        }
        var cel=document.getElementById('custom-theme-css');
        var sig=cel?(cel.textContent||''):'';
        var doLog=(sig!==lastCssSig);
        if (doLog) lastCssSig=sig;

        var customRules=cssRules(ss.custom);
        var baseRules=ss.base ? cssRules(ss.base) : [];
        var curThemeCls='theme-'+th;
        var rows=[];

        // ---- CSS-variable knobs ----
        var seenVar={}, varNames=[];
        for (var vi=0;vi<customRules.length;vi++){
            var apr=authoredPropsOf(customRules[vi].cssText);
            for (var ai=0;ai<apr.length;ai++){
                var nm=apr[ai];
                if (nm.indexOf('--')===0 && !seenVar[nm]){ seenVar[nm]=1; varNames.push(nm); }
            }
        }
        for (var vj=0;vj<varNames.length;vj++){
            var name=varNames[vj];
            var customVal=getVar(customRules, name, null);
            var baseVal=ss.base&&ss.base.ownerNode
                ? cssVarInText(ss.base.ownerNode.textContent||'', curThemeCls, name)
                : getVar(baseRules, name, curThemeCls);
            var eq=(customVal!==null && customVal!==null && customVal===baseVal);
            var consumers=consumersFor(name,[ss.base,ss.custom]);
            var collected={};
            var anyVirtual=false;
            for (var ci=0;ci<consumers.length;ci++){
                var mc=matchChunks(consumers[ci].selector);
                if (mc.virtual) anyVirtual=true;
                var elses=matchedEls(mc.chunks);
                if (!elses.length) continue;
                var mpp=measureProp(consumers[ci].prop);
                (collected[mpp]=collected[mpp]||[]).push.apply(collected[mpp], elses);
            }
            var totalEls=0; for (var mpK in collected) totalEls+=collected[mpK].length;
            var own=[]; for (var oi=0;oi<customRules.length;oi++) if (customRules[oi].style && customRules[oi].style.getPropertyValue(name)!=='') own.push(customRules[oi].style);
            var pretty=name.replace(/^--/,'');
            if (totalEls===0){
                if (consumers.length===0){ cnt.dead++; rows.push({status:'dead',label:pretty,eq:false,tunable:true,reason:'没有任何规则引用该变量'}); }
                else { cnt.virt++; rows.push({status:'virt',label:pretty,eq:eq,tunable:true,reason: anyVirtual?'仅伪元素(滚动条)消费，无法脚本验证':'消费选择器在当前样本无命中'}); }
                continue;
            }
            if (!own.length){ cnt.dead++; rows.push({status:'dead',label:pretty,eq:eq,tunable:true,reason:'变量未出现在 custom CSS'}); continue; }
            var changed=0;
            for (var mp2 in collected){
                var elsList=collected[mp2];
                changed+=countChanged(elsList, mp2, function(){
                    for (var jj=0;jj<own.length;jj++) own[jj].setProperty(name, 'rgb(0,250,1)');
                });
                for (var jr=0;jr<own.length;jr++) own[jr].setProperty(name, customVal);
            }
            if (changed>0){ cnt.eff++; if(eq)cnt.eq++; rows.push({status:'eff',label:pretty,eq:eq,tunable:true,reason:'影响 '+totalEls+' 个元素'}); }
            else { cnt.dead++; rows.push({status:'dead',label:pretty,eq:eq,tunable:true,reason:'命中但改值无响应'}); }
        }

        // ---- non-var authored declarations (typography / hljs / mermaid / puml) ----
        for (var ri=0;ri<customRules.length;ri++){
            var r=customRules[ri];
            if(!r.style||!r.selectorText) continue;
            var ck=matchChunks(r.selectorText);
            if(!ck.chunks.length && !ck.virtual) continue;
            var els=matchedEls(ck.chunks);
            var props=authoredPropsOf(r.cssText);
            for (var pi=0;pi<props.length;pi++){
                var dp=props[pi];
                if (dp.indexOf('--')===0) continue;
                var mp=measureProp(dp);
                var tunable=false;
                if ((dp==='font-size'||dp==='line-height') && ck.chunks.indexOf('body')>=0) tunable=true;
                if (dp==='font-size' && ck.chunks.indexOf('code')>=0) tunable=true;
                if ((dp==='margin-top'||dp==='margin-bottom') &&
                    (ck.chunks.indexOf('h1')>=0 || ck.chunks.indexOf('h2')>=0 || ck.chunks.indexOf('h3')>=0)) tunable=true;
                if (dp==='border-radius' && (ck.chunks.indexOf('pre')>=0||ck.chunks.indexOf('.code-block-wrapper')>=0)) tunable=true;
                if (ck.chunks.indexOf('.hljs')>=0 && (dp==='background'||dp==='color')) tunable=true;
                for (var cc=0;cc<ck.chunks.length;cc++){
                    var ch=ck.chunks[cc];
                    if (ch.indexOf('.hljs-')===0 && (dp==='color'||dp==='background')) tunable=true;
                    if (ch.indexOf('.mermaid-container')===0) tunable=true;
                    if (ch.indexOf('.puml-container')===0) tunable=true;
                    if (ch.indexOf('.plot-container')===0) tunable=true;
                    if (ch==='.puml-dark img' && dp==='filter') tunable=true;
                    if (ch==='.chat-block-wrap' && dp==='margin') tunable=true;
                    if (ch==='.chat-block' && dp==='border-radius') tunable=true;
                    if (ch==='.chat-body' && dp==='padding') tunable=true;
                }
                var label=(ck.chunks[0]||'rule')+' · '+dp;
                if (!els.length){
                    if (ck.virtual){ cnt.virt++; rows.push({status:'virt',label:label,eq:false,tunable:tunable,reason:'仅伪元素目标'}); }
                    continue;
                }
                var saved=r.style.getPropertyValue(dp);
                var changed=countChanged(els, mp, function(){ r.style.setProperty(dp, sentinelFor(dp)); });
                r.style.setProperty(dp, saved);
                if (changed>0){ cnt.eff++; if(tunable===false)cnt.hard++; rows.push({status:'eff',label:label,eq:false,tunable:tunable,reason:'影响 '+els.length+' 个元素'}); }
                else { cnt.dead++; rows.push({status:'dead',label:label,eq:false,tunable:tunable,reason:'改值无响应（可能被内联/!important 覆盖）'}); }
            }
        }

        if (doLog){
            try {
                console.log('[TAUDIT] START theme='+th+' baseRules='+baseRules.length+' customRules='+customRules.length+' eff='+cnt.eff+' dead='+cnt.dead+' virt='+cnt.virt+' eq='+cnt.eq+' hard='+cnt.hard);
                for (var li=0;li<rows.length;li++){
                    var rw=rows[li];
                    console.log('[TAUDIT] '+rw.status+'|'+rw.label+'|'+(rw.tunable===false?'hard':'knob')+'|'+(rw.eq?'eqdefault':'noeq')+'|'+rw.reason);
                }
                console.log('[TAUDIT] DONE');
            } catch(e){}
        }

        var html='<div>主题 <b>'+th+'</b> · base规则 '+baseRules.length+'<br/>'+
            '有效 🟢 <span style="color:#4ec9b0">'+cnt.eff+'</span>'+
            ' · 无效 🔴 <span style="color:#f14c4c">'+cnt.dead+'</span>'+
            ' · 伪元素 🔷 <span style="color:#4fc1ff">'+cnt.virt+'</span>'+
            ' · =内置 ⚪ <span style="color:#d7ba7d">'+cnt.eq+'</span></div>';
        html+='<hr style="border-color:#3a3a3a;margin:5px 0"/>';
        for (var hh=0;hh<rows.length;hh++){
            var r2=rows[hh];
            var icon= r2.status==='eff' ? '🟢' : (r2.status==='dead' ? '🔴' : '🔷');
            var hard= r2.tunable===false ? '<span style="color:#c586c0">[无滑块·写死]</span>' : '';
            var eqs= r2.eq ? '<span style="color:#d7ba7d"> =内置默认</span>' : '';
            html+='<div style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis;" title="'+r2.reason+'">'+icon+' <b style="color:#9cdcfe">'+r2.label+'</b> '+hard+eqs+' <span style="color:#777">'+r2.reason+'</span></div>';
        }
        suppressUntil = Date.now() + 150;
        body.innerHTML=html;
        var cp=document.getElementById(PANEL_ID); if(cp) cp.style.display='block';
    }

    function startObserver(){
        var target=document.body||document.documentElement;
        if (!target || !window.MutationObserver){ setTimeout(runAudit,1500); return; }
        var deb=null;
        var mo=new MutationObserver(function(){
            if (Date.now() < suppressUntil) return;
            if (deb) clearTimeout(deb);
            deb=setTimeout(runAudit,300);
        });
        mo.observe(target,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['class']});
        setTimeout(runAudit,900);
    }
    function boot(){ ensureFixture(); ensurePanel(); startObserver(); }
    if (document.readyState==='loading') document.addEventListener('DOMContentLoaded', boot);
    else setTimeout(boot,60);

    window.__themeAuditRemove=function(){
        var f=document.getElementById(FIX_ID); if(f&&f.parentNode) f.parentNode.removeChild(f);
        var p=document.getElementById(PANEL_ID); if(p&&p.parentNode) p.parentNode.removeChild(p);
    };
})();
