import axios from 'axios'

// The signed-in rider's height and weight, and their dependents' at this venue (Script0317).
export interface RiderMeasurementDto {
    heightIn: number | null
    weightLb: number | null
    measuredAt: string | null
}

export interface DependentMeasurementDto extends RiderMeasurementDto {
    id: string
    firstName: string
    lastName: string
}

export interface MyMeasurements {
    self: RiderMeasurementDto
    dependents: DependentMeasurementDto[]
}

export class RiderMeasurementService {
    private apiUrl: string

    constructor() {
        this.apiUrl = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
    }

    mine() {
        return axios.get<{ data: MyMeasurements }>(`${this.apiUrl}/RiderMeasurements/Mine`)
    }

    updateMine(req: { heightIn: number; weightLb: number }) {
        return axios.put<{ data: RiderMeasurementDto }>(`${this.apiUrl}/RiderMeasurements/Mine`, req)
    }

    updateDependent(id: string, req: { heightIn: number; weightLb: number }) {
        return axios.put<{ data: RiderMeasurementDto }>(`${this.apiUrl}/RiderMeasurements/Dependent/${id}`, req)
    }
}
