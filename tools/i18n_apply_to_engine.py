# -*- coding: utf-8 -*-
"""把引擎里面向用户的中文字面量批量接上 L()。

规则：
- docstring 不动；
- 相邻字符串隐式拼接视为一条，整体包一个 L()；
- 其余字符串字面量包成 L("...")，原有的 % 格式化留在 L() 外面，
  这样英文译文里同位置的占位符直接生效。
"""
import ast
import io
import re
import sys
import tokenize

CJK = re.compile(r'[一-鿿]')


def collect_docstrings(tree):
    """返回所有 docstring 节点的起始行号集合。

    注意：ast 的 col_offset 按 UTF-8 字节计，与 tokenize 的字符列不一致，
    所以这里只比行号，判定交给 token 流（见 is_docstring_token）。
    """
    out = set()
    for node in ast.walk(tree):
        if isinstance(node, (ast.Module, ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            body = getattr(node, 'body', None)
            if not body:
                continue
            first = body[0]
            if isinstance(first, ast.Expr) and isinstance(first.value, ast.Constant) \
                    and isinstance(first.value.value, str):
                out.add(first.value.lineno)
    return out


def is_docstring_token(toks, index):
    """判断某个字符串 token 是否独占一个逻辑行（docstring 的特征）。

    判据：向前跳过 NL/COMMENT/INDENT/DEDENT 后到达 NEWLINE/INDENT/DEDENT/ENDMARKER，
    向后跳过 NL/COMMENT 后紧跟 NEWLINE。
    """
    prev = None
    for x in reversed(toks[:index]):
        if x.type in (tokenize.NL, tokenize.COMMENT, tokenize.INDENT, tokenize.DEDENT):
            continue
        prev = x
        break
    if prev is not None and prev.type not in (tokenize.NEWLINE, tokenize.INDENT,
                                             tokenize.DEDENT, tokenize.ENDMARKER):
        return False
    for x in toks[index + 1:]:
        if x.type in (tokenize.NL, tokenize.COMMENT):
            continue
        return x.type == tokenize.NEWLINE
    return True


def main(path):
    src = io.open(path, encoding='utf-8').read()
    tree = ast.parse(src)
    doc_lines = collect_docstrings(tree)

    toks = list(tokenize.generate_tokens(io.StringIO(src).readline))

    # 计算每行的绝对偏移
    line_offsets = []
    pos = 0
    for line in src.splitlines(True):
        line_offsets.append(pos)
        pos += len(line)

    def abs_pos(rc):
        line, col = rc
        return line_offsets[line - 1] + col

    # 收集字符串 token 及其分组。
    # 隐式拼接只发生在同一逻辑行内，因此两个字符串 token 之间不能出现
    # NEWLINE 记号（只有括号内的 NL 才允许）——这一点用来避免把
    # 相邻两行里的独立语句错误地并成一组。
    groups = []
    idx = 0
    tok_index = {}
    for i, tk in enumerate(toks):
        tok_index[id(tk)] = i
    while idx < len(toks):
        t = toks[idx]
        if t.type == tokenize.STRING:
            group = [t]
            j = idx + 1
            while j < len(toks):
                nxt = toks[j]
                if nxt.type == tokenize.STRING:
                    gap = src[abs_pos(toks[j - 1].end):abs_pos(nxt.start)]
                    if gap.strip() == '' and set(gap) <= set(' \t\\\n\r'):
                        group.append(nxt)
                        j += 1
                        continue
                    break
                if nxt.type in (tokenize.NL, tokenize.COMMENT,
                                tokenize.INDENT, tokenize.DEDENT):
                    j += 1
                    continue
                break
            groups.append(group)
            idx = j
            continue
        idx += 1

    edits = []
    skipped = []
    for g in groups:
        raw = ''.join(t.string for t in g)
        if not CJK.search(raw):
            continue
        if g[0].start[0] in doc_lines and is_docstring_token(toks, tok_index[id(g[0])]):
            continue
        if 'f' in g[0].string[:2].lower():
            skipped.append((g[0].start[0], raw[:40]))
            continue
        start = abs_pos(g[0].start)
        end = abs_pos(g[-1].end)
        edits.append((start, end, 'L(' + src[start:end] + ')'))

    out = src
    for start, end, new in sorted(edits, key=lambda e: -e[0]):
        out = out[:start] + new + out[end:]

    io.open(path, 'w', encoding='utf-8', newline='\n').write(out)
    print('替换 %d 处，跳过 f-string %d 处' % (len(edits), len(skipped)))
    for ln, raw in skipped:
        print('  跳过 %d: %s' % (ln, raw))


if __name__ == '__main__':
    main(sys.argv[1])
