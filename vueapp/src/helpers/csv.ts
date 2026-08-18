/**
 * Client-side CSV export for the admin reports.
 *
 * The report screens already hold every row they render, so exporting them does not need a
 * round trip: the only thing that was ever missing was one place that gets the escaping right.
 * (ReportsTab.vue and Marketing.vue had each grown a private copy, and neither guarded against
 * spreadsheet formula injection.)
 *
 * Server-generated exports (the End of Day CSV, the Trackside CSV, payout statements) stay on
 * the server: those assemble figures the client never receives, and one of them is audited.
 */

/** Wraps a value per RFC 4180 and defuses spreadsheet formulas. */
function escapeCell(value: string | number | null | undefined): string {
    if (value === null || value === undefined) return ''
    let s = String(value)
    if (s === '') return ''
    // A leading = + - @ (or tab/CR) makes Excel and Sheets evaluate the cell as a FORMULA, and
    // report rows carry rider names, memos and event titles. A plain number is exempt so a
    // refund total is not disfigured into "'-12.34".
    if ('=+-@\t\r'.includes(s[0]) && !/^-?\d+(\.\d+)?$/.test(s)) s = "'" + s
    if (/[",\n\r]/.test(s)) s = '"' + s.replace(/"/g, '""') + '"'
    return s
}

/** One CSV section: an optional caption, an optional header row, and the body rows. */
export interface CsvSection {
    title?: string
    headers?: string[]
    rows: (string | number | null | undefined)[][]
}

export function buildCsv(headers: string[], rows: (string | number | null | undefined)[][]): string {
    return buildCsvSections([{ headers, rows }])
}

/** Several tables stacked in one file, separated by a blank line, the way an accountant expects. */
export function buildCsvSections(sections: CsvSection[]): string {
    const lines: string[] = []
    sections.forEach((section, i) => {
        if (i > 0) lines.push('')
        if (section.title) lines.push(escapeCell(section.title))
        if (section.headers) lines.push(section.headers.map(escapeCell).join(','))
        for (const row of section.rows) lines.push(row.map(escapeCell).join(','))
    })
    return lines.join('\r\n')
}

/**
 * Hands the browser a .csv download. The UTF-8 BOM is not optional: without it Excel on Windows
 * reads the file as the system codepage and mangles every accented name in it.
 */
export function downloadCsvText(filename: string, csv: string): void {
    const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = filename.endsWith('.csv') ? filename : `${filename}.csv`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
}

/** The common case: one table, one file. */
export function downloadCsv(
    filename: string,
    headers: string[],
    rows: (string | number | null | undefined)[][],
): void {
    downloadCsvText(filename, buildCsv(headers, rows))
}

/** Several tables, one file. */
export function downloadCsvSections(filename: string, sections: CsvSection[]): void {
    downloadCsvText(filename, buildCsvSections(sections))
}

/** Cents as plain decimal dollars for a CSV cell: no symbol, no thousands separator. */
export function csvMoney(cents: number | null | undefined): string {
    return ((cents ?? 0) / 100).toFixed(2)
}
