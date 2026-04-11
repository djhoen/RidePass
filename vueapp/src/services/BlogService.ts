import axios from 'axios'

export class BlogService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async getBlogFeeds() {
        return axios.get(`${this.apiUrl}/Blog/Feeds`);
    }

    async getBlogFeed(feedId: number) {
        return axios.get(`${this.apiUrl}/Blog/Feed?id=${feedId}`);
    }

    async getBlogFeedByUrl(url: string) {
        return axios.get(`${this.apiUrl}/Blog/Feed/Url?url=${url}`);
    }

    async getBlogPosts(feedId: number) {
        return axios.get(`${this.apiUrl}/Blog/Posts?feedId=${feedId}`);
    }

    async getBlogPost(postId: number) {
        return axios.get(`${this.apiUrl}/Blog/Post?id=${postId}`);
    }

    async getBlogPostByUrl(url: string) {
        return axios.get(`${this.apiUrl}/Blog/Post/Url?url=${url}`);
    }

    async getBlogPostSections(postId: number) {
        return axios.get(`${this.apiUrl}/Blog/PostSections?postId=${postId}`);
    }

    async createBlogPost(req: any) {
        return axios.post(`${this.apiUrl}/Blog/Admin/CreatePost`, req);
    }

    async updateBlogPost(req: any) {
        return axios.post(`${this.apiUrl}/Blog/Admin/UpdatePost`, req);
    }

    async createBlogPostSection(req: any) {
        return axios.post(`${this.apiUrl}/Blog/Admin/CreatePostSection`, req);
    }

    async updateBlogPostSection(req: any) {
        return axios.post(`${this.apiUrl}/Blog/Admin/UpdatePostSection`, req);
    }

    async deleteBlogPostSection(sectionId: number) {
        return axios.post(`${this.apiUrl}/Blog/Admin/DeletePostSection?id=${sectionId}`);
    }
}
