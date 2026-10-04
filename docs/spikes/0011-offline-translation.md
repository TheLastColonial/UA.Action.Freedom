# Spike: offline English to Ukrainian translation for the box label

Date: 2026-10-04. Gate for [plan 16](../plans/16-box-replacement-label.md), Increment 0. Resolves the choice left open by
[O29](../domain/decisions.md#o29) and [ADR 0011](../adr/0011-attested-boxes-are-replaced-not-edited.md).

**Requirement (O29):** the Ukrainian text on a label is machine translated, with **no external dependency**, and is
**not marked** as a machine translation. The adapter must fit the hosting constraints in
[`recommendations.md`](../recommendations.md): permanent free allowances only, scale to zero, cold starts accepted.

## Recommendation

1. **Do not use a machine translation model for anything that can be translated once by a person.** The fixed item
   categories (ten today) need ten human-written Ukrainian names, which is minutes of work for a Ukrainian speaker and
   is exact. Plan 05 added `ItemCategory.NameUk` (it "starts empty and is filled by the label work"). Fill it from the
   owner or a volunteer, not from a model.
2. **If free text must be translated, use Helsinki-NLP `opus-mt-en-uk` (INT8 ONNX) in-process**, behind
   `ILabelTranslator`, in the API image. It meets "no external dependency" and costs nothing, but **its quality on
   short item names is not good enough to go unmarked and unreviewed on a customs-facing document** (see Quality).
   That is a decision for the owner, listed under "Decisions needed".
3. **There is no usable Microsoft offline option.** Details below.

## 1. Microsoft offline options

| Option | Offline? | Verdict |
|---|---|---|
| **Azure Translator disconnected container** (Text Translation Standard) | Yes, runs on-premises with no connectivity at runtime | **Not usable.** Access is by an approval form, limited to "strategic customer or partner" organisations with zero-connectivity or strict-regulation scenarios, decided within 10 business days. It needs an **upfront calendar-year commitment-tier purchase**, which breaks "permanent free allowances only". Only visible in the portal once approved. It also runs as its own container, so would be a second always-on service, against scale to zero. |
| **Azure Translator connected container** | No. It must reach Azure for billing metering | Fails "no external dependency". Also billed per character. |
| **Azure Translator cloud API** | No | Fails O29 outright. Free tier exists (2M characters a month) but sends item text to a third party. |
| **Microsoft Translator app offline language packs** | Yes, but consumer apps only | The Windows app was retired in 2021. Android/iOS packs have no .NET API and no licence for server use. Not usable. |
| **Microsoft Foundry Local** (Linux, .NET SDK `Microsoft.AI.Foundry.Local`, SDK MIT, CLI under Microsoft Software License Terms) | Yes, runs catalogue models locally | Not a translator. It would mean shipping a general LLM, which is far heavier than a translation model, and quality in Ukrainian is not established. Not pursued further. |
| **Windows AI / Phi Silica style on-device APIs** | Yes | Windows and NPU-hardware only. Not Linux containers. |

Sources:
[Disconnected containers and eligibility](https://learn.microsoft.com/en-us/azure/ai-services/containers/disconnected-containers),
[Translator containers overview](https://learn.microsoft.com/en-au/azure/ai-services/translator/containers/overview),
[Foundry Local](https://learn.microsoft.com/en-us/azure/foundry-local/what-is-foundry-local),
[Translator app retirement](https://microsoft.com/translator/blog/2021/03/24/microsoft-translator-app-for-windows-desktop-will-be-retired-soon).
Conclusion: **no Microsoft-provided translation runtime is usable offline, from .NET, in a Linux container, at no
fixed cost.**

## 2. Fallback: OPUS-MT `en-uk` through ONNX Runtime

### Licence

- Model: `Helsinki-NLP/opus-mt-en-uk`. The model card says **Apache 2.0**. Many other OPUS-MT models are CC BY 4.0, and
  a community INT8 ONNX conversion of this family
  ([AndrPixel/opus-mt-mobile-onnx](https://huggingface.co/AndrPixel/opus-mt-mobile-onnx)) states CC BY 4.0. **The
  two disagree. Both permit commercial use with attribution**, so either is acceptable for a charity, but a
  third-party notice must ship in the image and the repository should record which licence the chosen file carries.
- Tokenizer: Marian SentencePiece models (`source.spm`, `target.spm`), shipped with the model, same licence.
  `Microsoft.ML.OnnxRuntime` is MIT. A SentencePiece binding for .NET would be needed (for example the
  `SentencePieceTokenizer` in `Microsoft.ML.Tokenizers`, MIT), which the implementation increment must verify.
- Training data is OPUS (mixed corpora). Not a practical constraint here, noted for completeness.

### Measurements

Run on 2026-10-04 on the developer's Windows 11 machine (16 logical CPUs, Docker Desktop), **inside a Linux
container limited to 1 vCPU and 2 GB**, which is roughly the smallest sensible Container Apps size
(0.25 vCPU / 0.5 GiB in `recommendations.md` is smaller still, so these numbers flatter it).
Export: `optimum-cli export onnx` with the with-past decoder, then dynamic INT8 quantisation (AVX2).
Decoding: greedy (`num_beams=1`), `max_new_tokens=64`. Test input: 20 realistic item lines.

| | fp32 ONNX | INT8 ONNX |
|---|---|---|
| Model files on disk | 1.2 GB | **221 MB** (encoder 51 MB, decoder 89 MB, decoder-with-past 86 MB, SentencePiece and vocab about 5 MB) |
| Process start plus model load | 5.5 s | **7.6 s** (includes Python and `transformers` imports, which a .NET host would not pay) |
| First 20-line batch | 6.4 s | 5.6 s |
| Warm 20-line batch (average of 5) | 7.2 s | **6.0 s** |
| 20 lines translated one at a time | 63.6 s | 38.0 s |
| Peak memory (RSS) | 1.3 GB | **0.76 GB** |

Reading these honestly:

- **Image size:** about **+220 MB** compressed-ish (INT8). The API image grows by roughly that plus the ONNX Runtime
  native library (tens of MB). This is hundreds of megabytes, as the plan's risk section warned, but not gigabytes.
  Better: put the translator in the **Manifest Worker** or its own queue-triggered worker, so the API image is
  unchanged and the cost is paid only where it is needed.
- **Cold start:** adds about **8 s** to loading the model on 1 vCPU. A label is printed at most a few times a day, so
  load lazily on first translation and accept the pause. The system already accepts cold starts of "a few seconds".
- **Latency:** a 20-line list takes about **6 s warm** batched on one vCPU. That is acceptable for "print label",
  **if translations are cached** (below) and not computed on every render. One-by-one would be 38 s and must not be
  done.
- **Memory:** 0.76 GB peak does **not** fit the 0.5 GiB sizing in `recommendations.md` section 2.2. The translating
  process needs at least 1 GiB, which affects the free-grant arithmetic (GiB-seconds). Isolating it in a worker
  that scales to zero keeps the cost to the minutes it is actually running.
- **Caveat on the figures:** measured with Python, `optimum` and PyTorch's tokenizer plumbing, because that was the
  fastest route within the time box. The ONNX Runtime graphs, and so the decoding cost, are the same ones a .NET host
  would run. Expect the .NET tokenizer and generation loop to change load time and the overhead around the model, not
  the order of magnitude. **The generation loop (greedy decode with a KV cache) is not provided by
  `Microsoft.ML.OnnxRuntime`: the implementation must write it.** That is a real cost, roughly a day, and the
  implementation increment should be sized for it. A community port ([AndrPixel](https://huggingface.co/AndrPixel/opus-mt-mobile-onnx))
  documents the same requirement, including that the pad token must be forbidden during generation or `en-uk` can
  emit empty output.

### Quality

INT8 output for the 20 test lines. These are plain item names of the kind a Loader will type.

| English | INT8 output | Assessment |
|---|---|---|
| Adult winter jackets, size M | Дорослі зимові куртки, розмір M | Good |
| Children's winter boots | Дитячі зимові чоботи. | Good |
| Cooking oil, sunflower, 1 litre | Олія для приготування їжі, соняшник, 1 літр | Good |
| Socks, thermal, men's | Шкарпетки, тепло, чоловічі | Understandable |
| Toothpaste and toothbrushes | Зубна паста і зубна щітка | Good |
| Paracetamol tablets 500mg | Таблички Parcetamol 500mg | **Wrong.** Drug name garbled, "tablets" became "little signs" |
| Water purification tablets | Таблички для очищення води | **Wrong word** for tablets |
| Tinned beans in tomato sauce | Підсмажені зерна в томатному соусі. | **Wrong.** "Fried grains" |
| First aid kit with tourniquet | Перший аптечок з турнікетом | Wrong gender, "turnstile" for tourniquet |
| Rechargeable LED torches | Перезаряджені лампочки | **Wrong.** "Recharged light bulbs" |
| Baby formula, stage 1 | Маля формула, фаза 1 | Poor |
| Hand-operated can openers | Ручне керування може відкривати | **Nonsense** ("manual control may open") |
| Camping stove with gas canisters | Ванна канапа в таборі. | **Nonsense** ("bath couch in the camp") |
| Portable power bank 20000mAh | Портативний банк 20000 метрів АГ | **Nonsense** (bank, metres) |
| Disposable gloves, nitrile | Незручні рукавички, зануда | **Nonsense** ("inconvenient gloves, bore") |
| Insulin pens (cold chain not required) | Ручки insulin (розмерленого ланцюга не потрібно) | **Wrong.** Drug untranslated, chain garbled |
| Sleeping bags, -10C rated | Сплячі сумки, -10C, що враховуються | Poor, "handbags" sense |

About **half of the lines are wrong or misleading**, and medical and equipment lines, the ones that matter most to a
border official, are among the worst. fp32 was no better, so quantisation is not the cause: short, out-of-context
noun phrases are this model's weakness (its published 50 BLEU is on Tatoeba sentences, which are not like this
input). **Under O29 the output is unmarked**, so a reader would take "Fried grains" or "bath couch" as the charity's
attestation of what is in the box. Plan 16's own risk section calls out this exact hazard.

Mitigations, from cheapest and most effective:

1. **Translate fixed categories by hand, once** (`NameUk`). The label shows category plus quantity, which is exact.
2. **Make free text optional on the label.** Show the item description only when a person has supplied its Ukrainian
   (a new optional `DescriptionUk` the Loader fills, or confirms against the machine suggestion at attestation time).
   Because the attesting Loader already vouches for the box, "review the Ukrainian before you sign" fits the existing
   act and removes the unmarked-machine-output risk. This does stay inside O29 ("machine translated") if the model
   proposes and the human confirms, but the owner may read O29 as ruling out any human step.
3. **Translate once, at attestation, and store the result** with the attested box. Then the label is deterministic and
   reprints identically, a replacement box re-translates only changed lines, and no model runs when printing.
4. A better model (for example NLLB-200 distilled, MADLAD, or an LLM) would help, but those are 600 MB to several GB
   and carry non-commercial or custom licences in some cases (NLLB-200 is CC BY-NC 4.0, which is unsuitable). Not
   recommended without a further, bigger spike.

### Proof of "no external dependency"

The benchmark was run with `docker run --network none`. The script first attempts a TCP connection to `1.1.1.1:53`,
which failed (`network check: OSError`), then translated all 20 lines. The model was baked into the image at build
time, nothing was fetched at run time. The same must be repeated in .NET as the plan's Increment 4 integration test
(`Category=Integration`, networking disabled).

## 3. Fit with the hosting constraints

| Constraint | Result |
|---|---|
| Permanent free allowances only | OPUS-MT INT8 costs £0 in licences and runs in the existing free Container Apps / Functions grants. The Microsoft container does not (annual commitment). |
| Scale to zero | Fits if the model is loaded lazily and translation runs in a scale-to-zero process. Loading takes about 8 s at 1 vCPU. |
| Cold start accepted | Yes, as long as translations are stored at attestation rather than computed per print. |
| Memory sizing | **Does not fit 0.5 GiB.** Needs about 1 GiB. Keep it out of the always-warm API replica. |
| Image size | +220 MB model. Prefer a worker image so the API image does not grow. |

## 4. Decisions needed from the owner

1. **Accept no Microsoft option.** None is usable. Confirm the fallback is the intended direction.
2. **Accept machine translation of free text at all, given the quality above?** Options: (a) as O29 says, unmarked and
   unreviewed (not recommended: roughly half of test lines wrong); (b) machine suggestion that the attesting Loader
   confirms or corrects before signing (recommended); (c) no free text on the label, categories and quantities only.
3. **Who supplies the ten Ukrainian category names** (`NameUk`)? A Ukrainian-speaking volunteer should, and should
   also confirm the hazard and "not carried" wording.
4. **Where does the translator run?** API image (+220 MB, 1 GiB needed), or the Manifest Worker / a new worker
   (recommended).
5. **Licence record.** Accept shipping the OPUS-MT model with a third-party notice, and decide Apache 2.0 versus
   CC BY 4.0 attribution wording from the exact file used.

## 5. Reproducing

Dockerfile and benchmark script are not committed (throwaway). Steps: `pip install "optimum[onnxruntime]" torch
sentencepiece`, `optimum-cli export onnx --model Helsinki-NLP/opus-mt-en-uk --task text2text-generation-with-past out`,
dynamic INT8 quantise with `ORTQuantizer`, then run a 20-line greedy decode in a `python:3.12-slim` container with
`--network none --cpus 1 -m 2g`.
