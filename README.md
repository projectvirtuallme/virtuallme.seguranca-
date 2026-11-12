# VirtuALLme Segurança (`com.virtuallme.seguranca`)

Pacote de editor Unity que deve estar presente em **todos os projetos VirtuALLme**.
Não depende de nada do projeto (funciona em Built-in, URP e HDRP, com ou sem VIROO) e não entra nos builds (é só Editor).

Menu: **VirtuALLme ▸ Segurança**. Na Scene View aparece também um painel flutuante **"VirtuALLme Segurança"**.

---

## 1 · Reparador de Cena — "um clique e a cena fica certinha"

| Botão | Atalho | O que faz |
|---|---|---|
| **Reparar cena agora** | `Ctrl+Alt+R` | Repõe o que se perdeu nas cenas abertas |
| **Guardar estado bom** | `Ctrl+Alt+G` | Regista "a cena está certinha assim" |
| Remover scripts em falta | — | Limpa componentes "Missing Script" |

**O que repara:**
- slots de material vazios ou "Missing" (procura pelo GUID e, se o asset mudou de sítio ou de GUID, pelo nome);
- **materiais apagados**: são recriados com o shader, as cores, os valores e as texturas que tinham. Se possível recebem o **GUID original**, e assim os prefabs e as outras cenas que os usavam também voltam a funcionar;
- **shaders partidos (rosa)**: o erro de shader e os materiais Built-in num projeto URP/HDRP passam para o shader do pipeline, mantendo a cor e as texturas (`_MainTex→_BaseMap`, `_Color→_BaseColor`…). Materiais só de leitura, dentro de FBX, são extraídos para `Assets/VirtuALLme_Recuperados/Materiais`;
- texturas que desapareceram dos materiais;
- meshes perdidas em MeshFilter / SkinnedMeshRenderer / MeshCollider;
- **"Missing Prefab"**: é ligado de novo ao prefab ou ao modelo certo, no mesmo sítio, com o mesmo pai, posição, rotação e escala;
- objetos que **desapareceram** desde o último "estado bom". Antes de os recriar, pergunta;
- skybox.

**Como funciona:** cada vez que guardas ou abres uma cena, é tirada uma "fotografia" leve dela para `VirtuALLme_Seguranca/Snapshots/`. A fotografia regista que material, mesh e prefab cada objeto usa e as propriedades de cada material. A informação boa nunca é substituída por informação partida.

- **Modo normal:** só preenche o que está vazio, partido ou rosa.
- **Modo forte** (opção no painel): repõe cores, texturas e materiais **exatamente** como no último "estado bom".

Tudo pode ser desfeito com `Ctrl+Z`. Quando uma cena abre com problemas, aparece um aviso a oferecer a reparação.

> Rotina recomendada: quando a cena estiver como deve ser, carrega **Guardar estado bom**.

## 2 · Auto-salvamento e Recuperação — "o Unity travou e não guardei"

- **Cópias automáticas:**
  - quando são feitas:
    - de 3 em 3 minutos (configurável);
    - antes de cada Play;
    - antes de cada recompilação;
  - em cada momento, é copiada cada cena com alterações por guardar;
  - é uma **cópia**, por isso a cena original não é tocada (não é um Ctrl+S forçado);
  - também grava materiais e assets alterados (opcional);
  - ficam guardadas as últimas 20 cópias de cada cena (configurável).
- **Onde ficam:** em `VirtuALLme_Seguranca/Backups/<Cena>_<id>/aaaaMMdd_HHmmss.unity`. A pasta fica fora de `Assets` e de `Temp`, por isso as cópias sobrevivem a crashes, a reinícios do PC e à limpeza do Unity.
- **Deteção de crash:** se a sessão anterior não fechou normalmente, a janela **Recuperar versão** abre sozinha quando o projeto volta a abrir. Mostra com ★ as cópias mais recentes do que o ficheiro guardado, ou seja, o trabalho que se teria perdido.
- **Restaurar** repõe a cena original nessa versão. Antes disso, o estado atual e o ficheiro do disco também são copiados, por isso nada se perde.
- **Abrir cópia** abre essa versão numa cena nova em `Assets/VirtuALLme_Recuperados/Cenas` e não toca no original.
- **Atalhos:** `Ctrl+Alt+B` faz uma cópia manual. Menu ▸ *Recuperar versão anterior…* abre a lista.

---

## Instalação

**A) Num projeto (pacote embutido):**
1. Copia a pasta `com.virtuallme.seguranca` para `<Projeto>/Packages/`.
2. Abre o Unity e espera que compile. Aparece o menu **VirtuALLme ▸ Segurança**.

**B) Para todos os projetos, a partir do GitHub (recomendado):**
1. Cria o repositório `virtuallme/com.virtuallme.seguranca` com o conteúdo desta pasta (incluindo os `.meta`).
2. Em cada projeto, abre o Package Manager e escolhe **+ ▸ Add package from git URL…**:
   ```
   https://github.com/<organizacao>/com.virtuallme.seguranca.git
   ```
   Em alternativa, junta ao `Packages/manifest.json`:
   ```json
   "com.virtuallme.seguranca": "https://github.com/<organizacao>/com.virtuallme.seguranca.git#1.0.0"
   ```
3. Para atualizar todos os projetos, basta publicar uma nova *tag* (`#1.0.1`).

**C) Disco partilhado:** no Package Manager escolhe **+ ▸ Add package from disk…** e seleciona o `package.json`.

### `.gitignore` sugerido

```
# backups locais (pesados, de cada máquina)
VirtuALLme_Seguranca/Backups/
```

Os `Snapshots/` podem ir para o Git. Assim, um colega que receba uma cena partida também a consegue reparar.

## Limitações a conhecer

- O reparador consegue repor um asset que ainda existe no projeto (mesmo que tenha mudado de nome de pasta ou de GUID) ou um material cuja receita foi registada. Não consegue reinventar um FBX, uma textura ou um prefab que foram apagados do disco: nesses casos indica o que falta na Console.
- Materiais transparentes convertidos de Built-in para URP podem precisar de ajustar *Surface Type* à mão.
- O auto-salvamento copia cenas. Um prefab aberto em Prefab Mode é coberto apenas pela opção "Gravar também materiais/assets alterados".
- Restaurar abre a cena em modo *Single*. Se trabalhas com várias cenas em simultâneo, volta a abrir as outras.

VirtuALLme · https://virtuall.me
