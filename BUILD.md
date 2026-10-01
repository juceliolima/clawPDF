# Compilando o clawPDF no Visual Studio 2026 / Insiders

A solução (`clawPDF.sln`) usa **.NET Framework 4.8** nos projetos C# e o toolset
**v145** nos projetos C++ quando aberta no Visual Studio 2026 (18.x). No Visual
Studio 2022 os projetos C++ continuam usando o v143 automaticamente.

## Componentes necessários (Visual Studio Installer → Modificar)

**Cargas de trabalho**
- Desenvolvimento para desktop com .NET
- Desenvolvimento para desktop com C++

**Componentes individuais**
- Pacote de direcionamento do .NET Framework 4.8 (*.NET Framework 4.8 targeting pack*)
- MSVC v145 – Ferramentas de build x64/x86 (mais recente)
- MSVC v145 – Ferramentas de build ARM64 (só se for compilar o clawmon para ARM64)
- Windows 11 SDK (qualquer versão recente)

**Extensão** (Extensões → Gerenciar Extensões)
- *Microsoft Visual Studio Installer Projects 2022* (compatível com VS 2022 e 2026).
  Necessária apenas para o projeto `clawPDF_setup` (.msi). Sem ela, o projeto
  aparece como "incompatível", mas o restante da solução compila normalmente.

## Compilar

1. Abra `clawPDF.sln`.
2. Se o VS oferecer "Retarget/Upgrade" dos projetos C++, pode aceitar ou ignorar;
   o toolset já é escolhido conforme a versão do VS.
3. Selecione **Release | Any CPU** (ou a plataforma desejada) e use
   *Compilar → Recompilar Solução*. Os pacotes NuGet (NLog, iText7, PdfToSvg.NET)
   são restaurados automaticamente.
4. A saída fica em `src/_Build/Release`.
5. Para gerar o instalador, clique com o botão direito em `clawPDF_setup` → *Build*.

## Observações

- O instalador agora exige o .NET Framework 4.8 (já presente no Windows 10 1903+,
  Windows 11 e Windows Server 2022; disponível para Windows Server 2016/2019).
- Os binários do port monitor (`src/lib/clawmon`) e do Ghostscript
  (`src/clawPDF/gsdll*.dll`) já vêm prontos no repositório.
