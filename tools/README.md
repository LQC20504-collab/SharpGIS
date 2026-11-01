# tools —— 开发辅助脚本

这些脚本**不参与程序运行**，也不会被复制到输出目录（`SharpGIS.csproj` 只收录
`Engine\` 与 `Models\` 等运行时需要的文件）。

| 脚本 | 用途 |
|---|---|
| `i18n_apply_to_engine.py` | 一次性改写工具：把 `Engine/camarkov_engine.py` 里面向用户的中文字面量批量包上 `L(...)`。已经跑过，保留是为了以后新增文案时能复用同一套规则（docstring 跳过、相邻字符串拼接合并）。 |
| `test_engine_lang.py` | 引擎中英双模式的回归测试：同一份合成数据跑 zh/en 两遍，校验英文报告零中文残留、中英两版的转移预算矩阵与精度指标完全一致。 |

## 跑法

```bash
# 引擎双语回归（需要 numpy / rasterio / matplotlib）
python tools/test_engine_lang.py

# 需要重新批量套 L(...) 时（会先改源码，改前务必备份）
python tools/i18n_apply_to_engine.py Engine/camarkov_engine.py
```

## 约定

- **C# 侧**：中文即键，英文译文集中在 `Services/LangDict*.cs`，用 `Lang.T("中文")` 取。
- **Python 侧**：同样以中文原文为键，译文在 `Engine/lang_en.py`，用 `L("中文")` 取。
- 两边都遵循「查不到译文就原样返回中文」，因此漏翻只会显示中文，不会出现空白或异常。
- **译文表里不能出现 `Lang.T` 于静态字段初始化器中**——静态字段在类初始化时求值，
  那时语言可能还没确定，会把中文固化。语言相关的取值一律用属性或方法惰性求值。
