"""Genera los datos de prueba (todos inventados, sin datos personales reales).

- padron_muestra.txt: padrón CSV Windows-1252 con las 78 columnas de server/app/estructura.json.
- Ocupacion/*.xlsx: reportes "Ocupación Edificios" con las variantes conocidas.
- Ocupacion/esperado.json: resultado esperado de cada Excel calculado con la lógica de
  la app Python original (app/ingest.py, copiada abajo sin cambios de comportamiento).
  Así las pruebas comparan la implementación C# contra la referencia Python, no contra sí misma.

Uso (desde esta carpeta): python generar_fixtures.py   (requiere openpyxl)
"""
import csv
import datetime
import hashlib
import json
import os
import re

import openpyxl

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))

# ======================================================================== padrón
cols = json.load(open(os.path.join(ROOT, 'server', 'app', 'estructura.json'), encoding='utf-8-sig'))['Columnas']
assert len(cols) == 78


def fila(estado, doc, soc, fecha, nombre='Nombre', apellido='Apellido', **extra):
    row = {c: f'{c[:3]}' for c in cols}
    row.update({'ESTADO': estado, 'DOCUMENTO': doc, 'NOMBRE SOCIEDAD': soc, 'FECHA EVENTO': fecha,
                'NOMBRE EMPLEADO': nombre, 'APELLIDO EMPLEADO': apellido})
    row.update(extra)
    return [row[c] for c in cols]


filas = [
    fila('Terminated', '900000001', 'BANCOLOMBIA', '28/09/2026', 'José', 'Peña Núñez'),
    fila('Terminated', '900000002', 'BANCOLOMBIA', '28/09/2026', 'Ana', 'Gómez', **{'NOMBRE POSICION': 'Analista, Senior'}),
    fila('Terminated', '900000003', 'NEQUI SA', '28/09/2026'),
    fila('Terminated', '900000004', 'BANCOLOMBIA', '27/09/2026'),
    fila('Con terminación de contrato', '900000005', 'BANCOLOMBIA', '28/09/2026'),
    fila('Con terminacion de contrato', '900000006', 'BANCOLOMBIA', '28/09/2026'),
    fila('Active', '900000007', 'BANCOLOMBIA', '28/09/2026'),
    fila('ReportNo-Show', '900000008', 'VALORES BANCOLOMBIA', '28/09/2026'),
    fila('Terminated', ' 900000009 ', 'BANCOLOMBIA', '28/09/2026', 'Íngrid', 'Ñáñez'),
    fila('Terminated', '900000010', 'BANCA DE INVERSION BANCOLOMBIA', '28/09/2026'),
]
with open(os.path.join(HERE, 'padron_muestra.txt'), 'w', encoding='cp1252', newline='') as f:
    w = csv.writer(f, delimiter=',', lineterminator='\r\n')
    w.writerow(cols)
    w.writerows(filas)
    # Fila corta (sin FECHA EVENTO): se cuenta pero no se usa.
    f.write('Terminated,900000011,CC\r\n')
    # Fila con comillas sin cerrar (el padrón real trae una): se descarta como malformada.
    f.write('Terminated,"900000012,BANCOLOMBIA\r\n')

# ======================================================================== Ocupación
OC = os.path.join(HERE, 'Ocupacion')
os.makedirs(OC, exist_ok=True)
HEADER = ['Nombres', 'Apellidos', 'Panel', 'Sede Administrativa', 'Cedula', 'Tarjeta de Acceso',
          'Ciudad', 'Empresa', 'First Swipe of Day', 'Last Swipe of Day']
dt = datetime.datetime


def libro(path, hojas):
    wb = openpyxl.Workbook()
    wb.remove(wb.active)
    for nombre, filas_hoja in hojas:
        ws = wb.create_sheet(nombre)
        for r in filas_hoja:
            ws.append(r)
    wb.save(path)


libro(os.path.join(OC, 'Ocupacion Edificios 3-2-2026.xlsx'), [('Sheet1', [
    ['Report Title:', 'Ocupacion Edificios'],
    ['Filter:', 'Todas las sedes'],
    ['Report Date:', dt(2026, 2, 4, 6, 0, 0)],
    ['Record Count:', 7],
    ['Exported By:', 'sistema'],
    [None],
    HEADER,
    ['Ana', 'Gómez', 'P1', 'Sede Norte', 1012345678, 123456, 'Medellín', 'ACME', dt(2026, 2, 3, 8, 15, 0), dt(2026, 2, 3, 17, 40, 5)],
    ['Ana', 'Gómez', 'P1', 'Sede Norte', 1012345678, 123456, 'Medellín', 'ACME', dt(2026, 2, 3, 8, 15, 0), dt(2026, 2, 3, 17, 40, 5)],
    ['Luis', 'Pérez  Díaz', 'P2', 'Sede Sur', '1098765432.0', '00123', 'Bogotá', 'ACME, S.A.', dt(2026, 2, 3, 7, 1, 59), None],
    ['Sin', 'Cedula', 'P1', 'Sede Norte', None, 1, 'Cali', 'ACME', dt(2026, 2, 3, 9, 0, 0), None],
    [None, None, None, None, None, None, None, None, None, None],
    ['Marta', 'Ruiz', 'P3', 'Sede Centro', 52123456.0, 7788.0, 'Cali', 'Beta', None, None],
    ['Juan\tCarlos', ' Ortiz\nLópez ', 'P4', 'Sede Norte', 1144556677, 990011, 'Medellín', 'ACME', dt(2026, 2, 3, 23, 59, 59), dt(2026, 2, 4, 0, 30, 0)],
])])

libro(os.path.join(OC, 'Ocupacion Edificios 4-2-2026.xlsx'), [
    ('Hoja1', [['Etiquetas de fila', 'Cuenta de Cedula'], ['ACME', 3]]),
    ('Sheet1', [
        HEADER,
        ['Pedro', 'Mora', 'P9', 'Sede Sur', 70100200, 55, 'Bogotá', 'Gamma', '2026-02-04 06:00:00', '2026-02-04 18:00:00'],
        ['Rosa', 'Vélez', 'P9', 'Sede Sur', 70100201, 56, 'Bogotá', 'Gamma', dt(2026, 2, 4, 6, 30, 0), None],
    ]),
])

libro(os.path.join(OC, 'Sin hoja Sheet1 5-2-2026.xlsx'), [
    ('Datos', [HEADER, ['Lina', 'Cruz', 'P1', 'Sede Norte', 33445566, 77, 'Cali', 'ACME', dt(2026, 2, 5, 7, 0, 0), None]]),
])

libro(os.path.join(OC, 'Invalido.xlsx'), [('Sheet1', [['Columna', 'Otra'], ['a', 'b']])])


# --------------------------- Referencia: lógica de app/ingest.py (app Python original)
DATE_RE = re.compile(r"(\d{4})-(\d{2})-(\d{2})")
FNAME_RE = re.compile(r"(\d{1,2})-(\d{1,2})-(\d{2,4})")
HEADER_MAP = {"nombres": "nombres", "apellidos": "apellidos", "panel": "panel",
              "sede administrativa": "sede_administrativa", "cedula": "cedula",
              "tarjeta de acceso": "tarjeta_acceso", "ciudad": "ciudad", "empresa": "empresa",
              "first swipe of day": "first_swipe", "last swipe of day": "last_swipe"}


def clean_text(value):
    if value is None:
        return ""
    text = str(value).replace("\t", " ").replace("\n", " ")
    return re.sub(r"\s+", " ", text).strip()


def clean_cedula(value):
    if value is None:
        return ""
    if isinstance(value, bool):
        return str(value)
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        return str(int(value)) if value.is_integer() else str(value)
    text = str(value).strip()
    if text.endswith(".0") and text[:-2].isdigit():
        text = text[:-2]
    return text


def clean_datetime(value):
    if value is None:
        return ""
    if isinstance(value, (datetime.datetime, datetime.date)):
        return value.strftime("%Y-%m-%d %H:%M:%S")
    return str(value).strip()


def extract_date(*candidates):
    for value in candidates:
        if not value:
            continue
        match = DATE_RE.search(str(value))
        if match:
            return match.group(0)
    return None


def date_from_filename(filename):
    base = os.path.basename(filename)
    for match in FNAME_RE.finditer(base):
        day, month, year = match.groups()
        year = int(year)
        if year < 100:
            year += 2000
        elif 200 <= year < 1000:
            year += 1800 + 100
        try:
            return datetime.date(year, int(month), int(day)).isoformat()
        except ValueError:
            continue
    return None


def detect_header(rows, max_scan=30):
    for idx, row in enumerate(rows[:max_scan]):
        cells = [clean_text(c).lower() for c in row]
        if "cedula" in cells and "nombres" in cells:
            return idx, [HEADER_MAP.get(c) for c in cells]
    return None, None


def parse_workbook(path):
    wb = openpyxl.load_workbook(path, read_only=True, data_only=True)
    try:
        ws = wb["Sheet1"] if "Sheet1" in wb.sheetnames else wb[wb.sheetnames[0]]
        rows = list(ws.iter_rows(values_only=True))
    finally:
        wb.close()
    header_idx, header_map = detect_header(rows)
    if header_idx is None:
        raise ValueError("No se encontro la fila de encabezado (Nombres/Cedula)")
    positions = {}
    for col, name in enumerate(header_map):
        if name and name not in positions:
            positions[name] = col
    fallback_date = date_from_filename(path)
    records, errors = [], 0
    for row in rows[header_idx + 1:]:
        if row is None or all(c is None or clean_text(c) == "" for c in row):
            continue

        def get(name):
            col = positions.get(name)
            if col is None or col >= len(row):
                return None
            return row[col]

        cedula = clean_cedula(get("cedula"))
        if not cedula:
            errors += 1
            continue
        first_swipe = clean_datetime(get("first_swipe"))
        last_swipe = clean_datetime(get("last_swipe"))
        r = {
            "fecha": extract_date(first_swipe, last_swipe, fallback_date),
            "nombres": clean_text(get("nombres")), "apellidos": clean_text(get("apellidos")),
            "panel": clean_text(get("panel")), "sede_administrativa": clean_text(get("sede_administrativa")),
            "cedula": cedula, "tarjeta_acceso": clean_cedula(get("tarjeta_acceso")),
            "ciudad": clean_text(get("ciudad")), "empresa": clean_text(get("empresa")),
            "first_swipe": first_swipe, "last_swipe": last_swipe,
        }
        raw = "|".join("" if p is None else str(p) for p in [
            r["cedula"], r["fecha"], r["panel"], r["sede_administrativa"], r["empresa"], r["ciudad"],
            r["tarjeta_acceso"], r["first_swipe"], r["last_swipe"], r["nombres"], r["apellidos"]])
        r["fingerprint"] = hashlib.sha1(raw.encode("utf-8", "ignore")).hexdigest()
        records.append(r)
    return records, errors


esperado = {}
for name in sorted(os.listdir(OC)):
    if not name.endswith('.xlsx'):
        continue
    try:
        recs, errs = parse_workbook(os.path.join(OC, name))
        esperado[name] = {'errores': errs, 'registros': recs}
    except ValueError as e:
        esperado[name] = {'error': str(e)}
json.dump(esperado, open(os.path.join(OC, 'esperado.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('ok:', {k: (len(v['registros']), v['errores']) if 'registros' in v else v['error'] for k, v in esperado.items()})
