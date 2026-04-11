export default {
    getParam(name: string): string | null {
        const urlParams = new URLSearchParams(window.location.search)
        return urlParams.get(name)
    },

    getParamInt(name: string): number {
        const val = this.getParam(name)
        return val ? parseInt(val) : 0
    }
}
